"""Offline verification of actual fixed-profile startup failures and raw pages.

No process creation, native loading, or debug APIs. Producer counters alone do
not establish the memory-byte and page-protection assertions in fatal cases.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--matrix", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    matrix = args.matrix.resolve()
    records = json.loads(matrix.read_text(encoding="utf-8-sig"))
    decoder_path = Path(__file__).with_name("observe-bootstrap-failure.py")
    spec = importlib.util.spec_from_file_location("owned_v4_decode", decoder_path)
    decoder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(decoder)
    checks = 0
    failures, reviewed = [], []

    def check(value, message):
        nonlocal checks
        checks += 1
        if not value:
            raise ValueError(message)

    expected = {(1, 0), (8, 0)} | {(s, i) for s in range(2, 8) for i in range(4)}
    cases = records["cases"]
    check(len(cases) == 26 and {(int(c["stage"]), int(c["index"])) for c in cases} == expected,
          "All 26 distinct compiled failure profiles must have actual results.")
    for case in cases:
        stage, index = int(case["stage"]), int(case["index"])
        current = {"stage": stage, "index": index, "passed": False}
        reviewed.append(current)
        try:
            directory = Path(case["directory"])
            fatal = stage in (5, 6, 7) or (stage in (3, 4) and index > 0)
            check(bool(case["fatalExpected"]) == fatal, "Runner classification differs from the independently defined matrix.")
            check(case["expectedProcessBehaviorMatched"] and not case.get("timedOut", False), "The runner did not observe the expected actual process behavior.")
            if fatal:
                observation = directory / "observation"
                result_path = observation / "observer-result.json"
                result = json.loads(result_path.read_text(encoding="utf-8-sig"))
                check(result["passed"] and result["exitObserved"] and result["fatalEventObserved"] and result["lastChanceObserved"],
                      "Actual fail-fast event, last chance, and exit must all be observed.")
                check(result["actualExitCode"] in (0xc0000409, 0xc0000602)
                      and result["actualExitCode"] == result["processHandleExitCode"] and result["allHeldInputsUnchanged"],
                      "Actual fatal exit and independently held process handle must agree.")
                check(sha((observation / "debug-events.jsonl").read_bytes()) == result["eventsSha256"], "Raw debug events changed.")
                terminal = [item for item in result["snapshots"] if item["reason"] == "last-chance-exception"]
                check(len(terminal) == 1, "One actual final exception snapshot is required.")
                terminal = terminal[0]
                raw = (observation / terminal["path"]).read_bytes()
                check(len(raw) == 4096 and sha(raw) == terminal["sha256"], "Terminal raw V4 differs from its observed full-byte hash.")
                value = decoder.decode_v4(raw)
                check(value == terminal["publication"] and value["processId"] == result["actualChildPid"], "Decoded V4 is not the actual observed child record.")
                check(terminal["permissionsStrictlyVerified"] and value["bootstrapModule"] == terminal["moduleBase"], "Full actual V4 memory owner and data permissions differ.")
                bodies = terminal["actualSourceBodies"]
                check(bodies["complete"] and len(bodies["functions"]) == 4 and bodies["moduleBase"] == value["targetModule"],
                      "Independent actual target image and four complete bodies are required.")
                shared_page = {body["address"] // 4096 for body in bodies["functions"]}
                check(len(shared_page) == 1, "This four-function protection expectation requires the actual fixed bodies to share their image page.")
                for kind, body in enumerate(bodies["functions"]):
                    check(body["kind"] == kind and body["recordTargetMatchesFixedExport"] and body["recordRvaMatchesFixedExport"]
                          and body["recordModuleMatchesActualMapping"] and body["publicationTargetModuleMatchesActualMapping"],
                          "A record-supplied address differs from independently parsed fixed exports and actual loaded module.")
                    disk, memory = ((observation / body[label]["path"]).read_bytes() for label in ("disk", "memory"))
                    check(len(disk) == len(memory) == body["byteSize"] and sha(disk) == body["disk"]["sha256"]
                          and sha(memory) == body["memory"]["sha256"], "Actual full source bytes differ from their raw artifacts.")
                    fixed = result["sourceDiskContract"]["functions"][kind]
                    check(disk.hex() == fixed["diskHex"] and body["rva"] == fixed["rva"] and body["endRva"] == fixed["endRva"],
                          "Observed body does not use the held fixed-export disk contract.")
                    row = value["rows"][kind]
                    prefix = (15, 18, 16, 15)[kind]
                    check(row["prefixBytes"] == prefix and row["target"] == value["targetModule"] + fixed["rva"]
                          and row["rva"] == fixed["rva"] and row["continuation"] == row["target"] + prefix,
                          "Fixed source prefix, target RVA and continuation differ from independent image facts.")
                    patch = bytearray(disk[:32])
                    patch[:14] = b"\xff\x25\0\0\0\0" + struct.pack("<Q", row["entry"])
                    patch[14:prefix] = b"\x90" * (prefix - 14)
                    check(bytes.fromhex(row["patchBytes"]) == patch, "Publication patch bytes differ from the independently constructed typed jump.")
                    wanted = bytearray(disk)
                    if kind < index or (kind == index and stage in (6, 7)):
                        wanted[:row["prefixBytes"]] = bytes.fromhex(row["patchBytes"])[:row["prefixBytes"]]
                    elif kind == index and stage == 5:
                        wanted[0] = bytes.fromhex(row["patchBytes"])[0]
                    check(memory == wanted, "Actual code bytes do not equal the stage-specific full/one-byte/untouched expected body.")
                    check(bytes.fromhex(row["originalBytes"]) == disk[:32] and sha(disk) == row["bodySha256"],
                          "The independently held full body and original bytes differ from V4.")
                    # All four actual fixtures share a page. A restore-failure
                    # leaves that actual page RWX, including the other bodies.
                    protect = 0x40 if stage == 6 else 0x20
                    segments = body["virtualMemorySegments"]
                    cursor = body["address"]
                    check(bool(segments), "Independent page-protection evidence must not be an empty list.")
                    for segment in segments:
                        check(segment["queriedAddress"] == cursor and segment["queryResult"] == 48
                              and segment["base"] <= cursor < segment["base"] + segment["regionSize"]
                              and segment["coveredBytes"] == min(body["address"] + body["byteSize"], segment["base"] + segment["regionSize"]) - cursor,
                              "Independent memory segments do not continuously cover the exact complete body.")
                        cursor += segment["coveredBytes"]
                    check(cursor == body["address"] + body["byteSize"], "Independent source-page coverage is incomplete.")
                    check(all(part["allocationBase"] == value["targetModule"] and part["state"] == 0x1000
                              and part["type"] == 0x1000000 and part["protect"] == protect for part in body["virtualMemorySegments"]),
                          "Independently observed actual source-page protection differs from the selected failure.")
                current.update(actualProcessId=result["actualChildPid"], actualExitCode=result["actualExitCode"], independentSourceReadback=True,
                               observerResultSha256=sha(result_path.read_bytes()))
            else:
                report_path = directory / "case.json"
                report = json.loads(report_path.read_text(encoding="utf-8-sig"))
                check(case["actualExitCode"] == 1 and not report["passed"] and not report["finished"] and report["sehCode"] == 0,
                      "A safe refusal must have the actual expected host exit and no hidden host exception.")
                initial = report["initialSnapshot"]
                data = (directory / initial["path"]).read_bytes()
                check(len(data) == 2162752 and sha(data) == initial["sha256"], "The actual complete initial snapshot changed.")
                check(struct.unpack_from("<4I", data) == (1, 2162752, 0xf0, 16), "Safe refusal must retain all four real initializer phases.")
                rows = [data[64 + k * 8448:64 + (k + 1) * 8448] for k in range(16)]
                value = decoder.decode_v4(rows[0][256:4352])
                for order, row in enumerate(rows):
                    check(struct.unpack_from("<5I", row) == (1, 8448, 4 + order // 4, order % 4, 0), "Safe-refusal initializer identity differs.")
                    returned, raw_return, caught = struct.unpack_from("<3I", row, 24)
                    check((returned, raw_return, caught) == (1, (0x81, 0xa5c3fe81, 0xe7, 0)[order % 4], 0),
                          "A no-write refusal must leave the typed native original behavior intact.")
                    for offset in (256, 4352):
                        observed = decoder.decode_v4(row[offset:offset + 4096])
                        check(observed["sourceWriteAttempts"] == observed["sourceWriteCompletions"] == observed["protectionOutstanding"] == 0
                              and observed["committedMask"] == 0, "Safe refusal wrote source bytes or abandoned page-protection responsibility.")
                current.update(actualProcessId=report["actualProcessId"], actualExitCode=case["actualExitCode"], independentSourceReadback=False,
                               limitation="No external terminal source-page readback in nonfatal cases; actual typed originals and V4 checked.",
                               hostResultSha256=sha(report_path.read_bytes()))
            failure = {1: 3, 8: 2, 2: 9, 3: 10, 4: 10, 5: 11, 6: 12, 7: 13}[stage]
            check(value["version"] == 4 and value["byteSize"] == 4096 and value["state"] == 6 and value["failure"] == failure,
                  "The exact intended failure must be the permanent terminal Unknown cause.")
            check(value["attachReason"] == value["startupStatic"] == value["claimed"] == value["profile"] == 1
                  and not value["overflow"] and value["processId"] == current["actualProcessId"], "Actual startup identity, failure log, or process identity differs.")
            mask = (1 << index) - 1
            check(value["configuredMask"] == (0 if stage in (1, 8) else mask if stage == 2 else 15)
                  and value["boundMask"] == (0 if stage in (1, 8, 2) else 15), "No-write configuration ownership differs from the selected permanent failure point.")
            check(value["committedMask"] == (0 if stage in (1, 8, 2) else mask), "The committed prefix mask differs from the selected ordered failure.")
            writes = 0 if stage in (1, 8, 2) else index + (1 if stage in (5, 6, 7) else 0)
            completed = writes - (1 if stage == 5 else 0)
            check(value["sourceWriteAttempts"] == writes and value["sourceWriteCompletions"] == completed
                  and value["protectionOutstanding"] == (1 if stage == 6 else 0), "Actual write/protection responsibility counters differ.")
            counters = ("protectAttempts", "protectSucceeded", "sourceWriteAttempts", "sourceBytesWritten", "readbackAttempts",
                        "readbackVerified", "restoreAttempts", "restoreSucceeded", "flushAttempts", "flushSucceeded", "protectionOutstanding")
            for kind, row in enumerate(value["rows"]):
                wanted = (0,) * 11
                if stage in range(3, 8) and kind < index:
                    wanted = (1, 1, 1, row["prefixBytes"], 1, 1, 1, 1, 1, 1, 0)
                elif stage in range(3, 8) and kind == index:
                    wanted = {
                        3: (0,) * 11,
                        4: (1, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0),
                        5: (1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0),
                        6: (1, 1, 1, row["prefixBytes"], 1, 1, 0, 0, 0, 0, 1),
                        7: (1, 1, 1, row["prefixBytes"], 1, 1, 1, 1, 0, 0, 0),
                    }[stage]
                check(tuple(row[name] for name in counters) == wanted,
                      "Per-kind actual API/write/readback/restore/flush counters do not identify the exact selected failure point.")
                if stage in range(3, 8):
                    check(row["flags"] == (7 if kind < index else 3)
                          and row["observedProtection"] == (0x40 if stage == 6 and kind == index else 0x20),
                          "Prepared/bound/committed ownership or the row's observed protection differs.")
            check(0 < value["eventCount"] <= 64 and not any(bytes.fromhex(value["reserved"]))
                  and all(not any(event.values()) for event in value["events"][value["eventCount"]:]),
                  "The complete publication event bounds and unused storage must be valid.")
            events = value["events"][:value["eventCount"]]
            check([e["sequence"] for e in events] == list(range(1, len(events) + 1)), "Actual event sequence has gaps or duplicates.")
            first_failure = next(e for e in events if e["stage"] in (17, 18))
            check(first_failure["failure"] == failure and not any(e["stage"] == 16 for e in events), "A failed startup incorrectly reported Ready or replaced its original failure.")
            if stage not in (1, 8, 2):
                configure = [e["sequence"] for e in events if e["stage"] == 6]
                protects = [e["sequence"] for e in events if e["stage"] == 8]
                check(len(configure) == 4 and (not protects or max(configure) < min(protects)), "All four permanent configurations must precede any actual protect/write.")
                for kind in range(4):
                    actual = [e["stage"] for e in events if e["kind"] == kind and 8 <= e["stage"] <= 17]
                    selected = {3: [17], 4: [8, 9, 13, 17], 5: [8, 9, 10, 11, 13, 17],
                                6: [8, 9, 10, 11, 12, 17], 7: [8, 9, 10, 11, 12, 13, 17]}[stage]
                    wanted = [8, 9, 10, 11, 12, 13, 14, 15] if kind < index else selected if kind == index else []
                    check(actual == wanted, "Per-kind chronological native event stages do not match the exact failure point.")
            current["passed"] = True
        except Exception as error:
            current["error"] = str(error)
            failures.append({"stage": stage, "index": index, "error": str(error)})
    result = {"kind": "offline-owned-startup-fault-verification", "passed": not failures and all(item["passed"] for item in reviewed),
              "matrixPath": str(matrix), "matrixSha256": sha(matrix.read_bytes()), "verifierSha256": sha(Path(__file__).read_bytes()),
              "decoderSha256": sha(decoder_path.read_bytes()), "checks": checks, "cases": reviewed, "failures": failures,
              "gameExecuted": False, "installed": False, "productionAdmissionProved": False}
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
    print(json.dumps({"passed": result["passed"], "checks": checks, "cases": len(reviewed), "failures": failures}))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
