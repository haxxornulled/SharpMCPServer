from __future__ import annotations

import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

PROJECT_ROOT = Path(__file__).resolve().parents[1]
SRC_ROOT = PROJECT_ROOT / "src"
if str(SRC_ROOT) not in sys.path:
    sys.path.insert(0, str(SRC_ROOT))

from mcpserver_agentrouter_bridge.bridge import (  # noqa: E402
    DEFAULT_LIBRARY_ENV,
    BridgeStatusCode,
    encode_request_payload,
    resolve_library_path,
)


class BridgePathTests(unittest.TestCase):
    def test_resolve_library_path_from_explicit_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            candidate = Path(tmp_dir) / "bridge.dll"
            candidate.write_text("native", encoding="utf-8")

            resolved = resolve_library_path(candidate)

            self.assertEqual(resolved, candidate.resolve())

    def test_resolve_library_path_from_env_directory(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            base = Path(tmp_dir)
            candidate_name = next(iter(self._candidate_names()))
            candidate = base / candidate_name
            candidate.write_text("native", encoding="utf-8")

            with mock.patch.dict(os.environ, {DEFAULT_LIBRARY_ENV: str(base)}, clear=False):
                resolved = resolve_library_path()

            self.assertEqual(resolved, candidate.resolve())

    def test_encode_request_payload_is_compact_json(self) -> None:
        payload = encode_request_payload({"objective": "hello", "metadata": {"agent.workflowMode": "deterministic"}})

        self.assertEqual(
            payload.decode("utf-8"),
            '{"objective":"hello","metadata":{"agent.workflowMode":"deterministic"}}',
        )

    def test_status_codes_are_stable(self) -> None:
        self.assertEqual(BridgeStatusCode.SUCCESS, 0)
        self.assertEqual(BridgeStatusCode.INVALID_ARGUMENTS, 1)
        self.assertEqual(BridgeStatusCode.EXECUTION_FAILURE, 2)
        self.assertEqual(BridgeStatusCode.CANCELLED, 3)

    @staticmethod
    def _candidate_names() -> tuple[str, ...]:
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


if __name__ == "__main__":
    unittest.main()
