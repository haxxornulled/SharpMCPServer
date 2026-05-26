from __future__ import annotations

import ctypes
import json
import os
import sys
import threading
from ctypes import POINTER, c_int, c_void_p, byref
from enum import IntEnum
from pathlib import Path
from typing import Any, Mapping

DEFAULT_LIBRARY_ENV = "MCP_SERVER_AGENTROUTER_NATIVE_LIBRARY"


class BridgeStatusCode(IntEnum):
    SUCCESS = 0
    INVALID_ARGUMENTS = 1
    EXECUTION_FAILURE = 2
    CANCELLED = 3


class BridgeError(RuntimeError):
    def __init__(self, status_code: int, message: str) -> None:
        self.status_code = status_code
        self.status_name = _status_name(status_code)
        super().__init__(f"{message} (status={self.status_name}, code={status_code})")


def encode_request_payload(request: Mapping[str, Any]) -> bytes:
    payload = json.dumps(request, ensure_ascii=False, separators=(",", ":"))
    return payload.encode("utf-8")


def resolve_library_path(library_path: str | os.PathLike[str] | None = None) -> Path:
    explicit_path = os.fspath(library_path) if library_path is not None else os.environ.get(DEFAULT_LIBRARY_ENV)
    if explicit_path:
        candidate = Path(explicit_path).expanduser()
        resolved = _resolve_candidate(candidate)
        if resolved is not None:
            return resolved
        raise FileNotFoundError(f"Native AgentRouter library was not found at '{candidate}'.")

    searched_paths: list[Path] = []
    for base_directory in _candidate_base_directories():
        for candidate_name in _candidate_library_names():
            candidate = base_directory / candidate_name
            searched_paths.append(candidate)
            if candidate.is_file():
                return candidate.resolve()

    raise FileNotFoundError(
        "Could not locate the NativeAOT AgentRouter library. "
        f"Set {DEFAULT_LIBRARY_ENV} or pass library_path explicitly. "
        f"Searched: {', '.join(str(path) for path in searched_paths)}"
    )


class AgentRouterBridge:
    def __init__(self, library_path: str | os.PathLike[str] | None = None) -> None:
        self._library_path = resolve_library_path(library_path)
        self._library = _load_library(self._library_path)
        self._close_lock = threading.Lock()
        self._closed = False

    @property
    def library_path(self) -> Path:
        return self._library_path

    def run(self, request: Mapping[str, Any]) -> dict[str, Any]:
        request_bytes = encode_request_payload(request)
        response_bytes = self.run_json(request_bytes)
        try:
            return json.loads(response_bytes.decode("utf-8"))
        except json.JSONDecodeError as exc:
            raise BridgeError(BridgeStatusCode.EXECUTION_FAILURE, f"Native bridge returned invalid JSON: {exc.msg}") from exc

    def run_json(self, request_json: bytes | bytearray | memoryview | str) -> bytes:
        request_bytes = _coerce_request_bytes(request_json)
        if len(request_bytes) > _INT32_MAX:
            raise BridgeError(BridgeStatusCode.INVALID_ARGUMENTS, "Request payload is too large for the bridge ABI.")

        with self._close_lock:
            self._ensure_open()
            input_buffer = ctypes.create_string_buffer(request_bytes)
            output_ptr = c_void_p()
            output_len = c_int()

            status = self._library.agent_router_run(
                ctypes.cast(input_buffer, c_void_p),
                len(request_bytes),
                byref(output_ptr),
                byref(output_len),
            )

            if status != BridgeStatusCode.SUCCESS:
                raise BridgeError(status, "agent_router_run failed")

            if not output_ptr.value or output_len.value < 0:
                raise BridgeError(BridgeStatusCode.EXECUTION_FAILURE, "Native bridge returned an empty payload.")

            try:
                return ctypes.string_at(output_ptr, output_len.value)
            finally:
                self._library.agent_router_free(output_ptr)

    def shutdown(self) -> None:
        with self._close_lock:
            if self._closed:
                return

            status = self._library.agent_router_shutdown()
            if status != BridgeStatusCode.SUCCESS:
                raise BridgeError(status, "agent_router_shutdown failed")

            self._closed = True

    def close(self) -> None:
        self.shutdown()

    def __enter__(self) -> AgentRouterBridge:
        return self

    def __exit__(self, exc_type: type[BaseException] | None, exc: BaseException | None, tb: Any) -> bool:
        try:
            self.shutdown()
        except BridgeError:
            if exc_type is None:
                raise
        return False

    def _ensure_open(self) -> None:
        if self._closed:
            raise BridgeError(BridgeStatusCode.INVALID_ARGUMENTS, "The bridge has already been shut down.")


def call_agent_router(request: Mapping[str, Any], library_path: str | os.PathLike[str] | None = None) -> dict[str, Any]:
    with AgentRouterBridge(library_path=library_path) as bridge:
        return bridge.run(request)


def _load_library(library_path: Path) -> ctypes.CDLL:
    library = ctypes.CDLL(str(library_path))

    library.agent_router_run.argtypes = [c_void_p, c_int, POINTER(c_void_p), POINTER(c_int)]
    library.agent_router_run.restype = c_int

    library.agent_router_free.argtypes = [c_void_p]
    library.agent_router_free.restype = None

    library.agent_router_shutdown.argtypes = []
    library.agent_router_shutdown.restype = c_int

    return library


def _coerce_request_bytes(request_json: bytes | bytearray | memoryview | str) -> bytes:
    if isinstance(request_json, bytes):
        return request_json
    if isinstance(request_json, bytearray):
        return bytes(request_json)
    if isinstance(request_json, memoryview):
        return request_json.tobytes()
    if isinstance(request_json, str):
        return request_json.encode("utf-8")
    raise TypeError("request_json must be bytes, bytearray, memoryview, or str")


def _status_name(status_code: int) -> str:
    try:
        return BridgeStatusCode(status_code).name.lower()
    except ValueError:
        return f"unknown({status_code})"


def _resolve_candidate(candidate: Path) -> Path | None:
    if candidate.is_file():
        return candidate.resolve()
    if candidate.is_dir():
        for candidate_name in _candidate_library_names():
            nested = candidate / candidate_name
            if nested.is_file():
                return nested.resolve()
    return None


def _candidate_base_directories() -> tuple[Path, ...]:
    module_directory = Path(__file__).resolve().parent
    cwd = Path.cwd()
    return (
        module_directory,
        module_directory / "native",
        cwd,
        cwd / "native",
    )


def _candidate_library_names() -> tuple[str, ...]:
    if sys.platform == "win32":
        return (
            "MCPServer.AgentRouter.PythonBridge.Native.dll",
            "MCPServer.AgentRouter.PythonBridge.Native",
        )

    if sys.platform == "darwin":
        return (
            "libMCPServer.AgentRouter.PythonBridge.Native.dylib",
            "MCPServer.AgentRouter.PythonBridge.Native.dylib",
        )

    return (
        "libMCPServer.AgentRouter.PythonBridge.Native.so",
        "MCPServer.AgentRouter.PythonBridge.Native.so",
    )


_INT32_MAX = 2_147_483_647
