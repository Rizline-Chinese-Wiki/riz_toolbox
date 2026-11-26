"""Run the public same-session reader against copies of its synthetic example."""

from datetime import datetime
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


DIAGNOSTICS = Path(__file__).resolve().parent
EXAMPLE = DIAGNOSTICS / "examples" / "synthetic-complete"
PREFIX = "[probe.sdk15.local-result-score] Sdk15LocalResult "
LOCAL_CHART = "riztoolbox.local/probe.sdk15.local-result-score/one-note-local/chart/one-note"
OFFICIAL_CHART = "chart.PastelLines.RekuMochizuki.0.EZ"
STOCK_JUDGEMENT_PLUGIN = "probe.r24.stock-judgement"


class SameSessionCliTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.fixture = Path(temporary.name) / "example"
        shutil.copytree(EXAMPLE, self.fixture)
        self.events = self.fixture / "host" / "events-000001.jsonl"
        self.rows = [json.loads(line) for line in self.events.read_text(encoding="utf-8-sig").splitlines()]

    def local_event(self, kind, phase=None):
        for row in self.rows:
            if not row["message"].startswith(PREFIX):
                continue
            payload = json.loads(row["message"][len(PREFIX):])
            if payload["kind"] == kind and (phase is None or payload["data"].get("phase") == phase):
                return row, payload
        self.fail(f"The synthetic example is missing {kind} ({phase}).")

    def replace_payload(self, row, payload):
        row["message"] = PREFIX + json.dumps(payload, separators=(",", ":"))

    def write_events(self):
        self.events.write_text("".join(json.dumps(row) + "\n" for row in self.rows), encoding="utf-8")

    def run_cli(self, exit_code, status, official_controls="verified"):
        result = subprocess.run(
            [sys.executable, "-B", str(DIAGNOSTICS / "check_same_session.py"),
             "--events", str(self.fixture / "host"), "--watch-output", str(self.fixture / "watch-output")],
            capture_output=True, encoding="utf-8", timeout=15,
        )
        self.assertEqual(result.returncode, exit_code, result.stdout + result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual(report["status"], status, report)
        self.assertEqual(report["official"]["controls"], official_controls, report)
        self.assertEqual(report["official"]["endReason"], "Completed", report)
        return report

    def assert_partial(self, official_controls="verified"):
        self.write_events()
        report = self.run_cli(2, "partial", official_controls)
        self.assertTrue(report["missing"], report)
        return report

    def replace_stock_judgement(self, resumed_revision_offset=0):
        resumed = next(row for row in self.rows if row["message"].startswith("Observed lifecycle Resumed: "))
        resumed_revision = int(resumed["message"].split("; revision=", 1)[1].split(";", 1)[0])
        host_prefix = "Judgement identity sample: "
        for row in self.rows:
            if not row["message"].startswith(host_prefix):
                continue
            witness = json.loads(row["message"][len(host_prefix):])
            note = witness["RuntimeNote"]
            if note["ObservedRunId"] != resumed["context"]["runId"]:
                continue
            sample = {
                "sessionId": note["SessionId"], "runId": note["ObservedRunId"], "chartId": OFFICIAL_CHART,
                "sceneGeneration": witness["SceneGeneration"], "resumedRevision": resumed_revision + resumed_revision_offset,
                "lifecycleRevisionAtEntry": witness["LifecycleRevisionAtEntry"], "invocation": witness["Invocation"],
                "runtimeNoteSequence": note["Sequence"], "sourceId": "synthetic-stock-tap",
                "originalReturned": witness["OriginalReturned"],
            }
            row["message"] = f"[{STOCK_JUDGEMENT_PLUGIN}] StockJudgementProbe POST_RESUME " + json.dumps(sample)
            row["context"] = {"pluginId": STOCK_JUDGEMENT_PLUGIN}
            return
        self.fail("The synthetic example is missing the official post-Resume host judgement.")

    def test_complete_synthetic_example(self):
        report = self.run_cli(0, "verified")
        self.assertEqual(report["local"]["outcome"], "verified")
        self.assertEqual(report["missing"], [])

    def test_local_result_cannot_precede_acceptance(self):
        request, _ = self.local_event("play-requested")
        receipt, _ = self.local_event("play-receipt")
        result, _ = self.local_event("local-result-read")
        request_time = datetime.fromisoformat(request["timestamp"])
        receipt_time = datetime.fromisoformat(receipt["timestamp"])
        result["timestamp"] = (request_time + (receipt_time - request_time) / 2).isoformat()
        self.rows.remove(result)
        self.rows.insert(self.rows.index(request) + 1, result)
        for sequence, row in enumerate(self.rows, 1):
            row["sequence"] = sequence
        self.assert_partial()

    def test_sdk_result_must_match_terminal_comparison(self):
        row, payload = self.local_event("local-result-read")
        payload["data"]["result"]["LocalSessionId"] = "2" * 32
        self.replace_payload(row, payload)
        self.assert_partial()

    def test_missing_row_between_official_finish_and_local_request(self):
        trigger, _ = self.local_event("trigger-ready")
        self.rows.remove(trigger)
        self.assert_partial()

    def test_official_score_digest_must_match_with_equal_counts(self):
        after_row, after = self.local_event("score-sampled", "after")
        final_row, final = self.local_event("score-isolation")
        changed_hash = "f" * 64
        after["data"]["sample"]["Sha256"] = changed_hash
        final["data"]["after"]["Sha256"] = changed_hash
        self.replace_payload(after_row, after)
        self.replace_payload(final_row, final)
        self.assert_partial()

    def test_framework_start_must_match_local_chart(self):
        start = next(row for row in self.rows if row["eventCode"] == "game.run.started" and LOCAL_CHART in row["message"])
        start["message"] = start["message"].replace(LOCAL_CHART, "chart.synthetic.different.0.EZ", 1)
        self.assert_partial()

    def test_stock_judgement_probe_verifies_post_resume_note(self):
        self.replace_stock_judgement()
        self.write_events()
        report = self.run_cli(0, "verified")
        self.assertEqual(report["official"]["postResumeJudgements"], 1)

    def test_stock_judgement_probe_requires_current_resumed_revision(self):
        self.replace_stock_judgement(resumed_revision_offset=-1)
        report = self.assert_partial(official_controls="partial")
        self.assertEqual(report["official"]["postResumeJudgements"], 0)


if __name__ == "__main__":
    unittest.main()
