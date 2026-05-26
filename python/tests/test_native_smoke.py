from __future__ import annotations

import os
import sys
import unittest
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
SRC_ROOT = PROJECT_ROOT / "src"
if str(SRC_ROOT) not in sys.path:
    sys.path.insert(0, str(SRC_ROOT))

from mcpserver_agentrouter_bridge.bridge import DEFAULT_LIBRARY_ENV, AgentRouterBridge, resolve_library_path  # noqa: E402


class NativeBridgeSmokeTests(unittest.TestCase):
    def test_round_trip_against_native_bridge(self) -> None:
        library_hint = os.environ.get(DEFAULT_LIBRARY_ENV)
        if not library_hint:
            self.skipTest(f"Set {DEFAULT_LIBRARY_ENV} to run the native smoke test.")

        library_path = resolve_library_path(library_hint)
        with AgentRouterBridge(library_path=library_path) as bridge:
            response = bridge.run(
                {
                    "objective": "review the workspace",
                    "metadata": {
                        "agent.workflowMode": "deterministic",
                    },
                }
            )

        self.assertIsInstance(response, dict)
        self.assertIn("status", response)
        self.assertIn(response["status"], {"completed", "denied"})
        self.assertIn("message", response)
        self.assertIsInstance(response["message"], str)


if __name__ == "__main__":
    unittest.main()
