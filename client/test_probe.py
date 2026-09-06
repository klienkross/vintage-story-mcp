import io
import json
import unittest
import urllib.error
from unittest.mock import patch

import probe


class ProbeTests(unittest.TestCase):
    def test_fetch_state_decodes_success_json(self):
        response = io.BytesIO(json.dumps({"ok": True, "player": {}}).encode())
        response.__enter__ = lambda self: self
        response.__exit__ = lambda *args: None
        with patch("urllib.request.urlopen", return_value=response):
            self.assertTrue(probe.fetch_state()["ok"])

    def test_fetch_state_decodes_http_error_contract(self):
        body = io.BytesIO(b'{"ok":false,"code":"player_unavailable"}')
        error = urllib.error.HTTPError(probe.DEFAULT_BASE_URL, 503, "unavailable", {}, body)
        with patch("urllib.request.urlopen", side_effect=error):
            payload = probe.fetch_state()
        self.assertEqual(payload, {"ok": False, "code": "player_unavailable"})


if __name__ == "__main__":
    unittest.main()
