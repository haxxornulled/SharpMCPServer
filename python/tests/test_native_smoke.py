from __future__ import annotations

import sys
import unittest
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
SRC_ROOT = PROJECT_ROOT / "src"
if str(SRC_ROOT) not in sys.path:
    sys.path.insert(0, str(SRC_ROOT))

from mcpserver_agentrouter_bridge.bridge import AgentRouterBridge, resolve_library_path  # noqa: E402


class NativeBridgeSmokeTests(unittest.TestCase):
    def test_round_trip_against_native_bridge(self) -> None:
        try:
            library_path = resolve_library_path()
        except FileNotFoundError:
            self.skipTest("Native library has not been synced into the package-native folder yet.")

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
