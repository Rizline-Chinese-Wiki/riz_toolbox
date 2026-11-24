"""Recheck the frozen integration and the documents handed to the next task."""
import argparse
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import unquote


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def main(output):
    output = Path(output).resolve()
    require(not output.exists(), "Use a new handoff directory")
    output.mkdir(parents=True)
    case = Path(__file__).resolve().parent
    repo = case.parents[2]
    result, index = read(case / "test-result.json"), read(case / "evidence-index.json")
    require(result["passed"] and result == index["summary"], "The copied integration summary differs")
    seal = Path("D:/riz-build-cache-next/foundation-stock-source-integration-seal-r2-20261031")
    for name in ("test-result.json", "evidence-index.json"):
        require(digest(case / name) == digest(seal / name), "The report copy differs from the actual seal")
    for row in index["verifiedFiles"]:
        require(digest(row["path"]) == row["sha256"], "A sealed input/output changed: " + row["path"])

    documents = [path for path in case.iterdir() if path.is_file() and path.name != "handoff-integrity.json"]
    documents += [repo / ".docs/CURRENT-CHECKPOINT.md", repo / ".docs/foundation-expansion/NEXT-FUNCTIONAL-GAPS.md",
                  repo / ".docs/foundation-expansion/N2-STOCK-SOURCE-NEXT.md"]
    for name in ("playback-source-group-20261031", "playback-stock-native-20261031", "playback-stock-metadata-20261031"):
        documents += list((case.parent / name).glob("*.md"))
    hashes, links = [], []
    for path in sorted(set(documents)):
        require(path.is_file(), "Missing handoff file: " + str(path))
        hashes.append({"path": str(path), "sha256": digest(path), "bytes": path.stat().st_size})
        if path.suffix != ".md":
            continue
        body = path.read_text(encoding="utf-8-sig")
        # Historical checkpoint sections keep their own original conclusions.
        if path.name == "CURRENT-CHECKPOINT.md":
            body = body.split("以下为上一批broker/owned/Play整合", 1)[0]
        elif path.name == "NEXT-FUNCTIONAL-GAPS.md":
            body = body.split("以下为2026-10-30", 1)[0]
        body = re.sub(r"```.*?```", "", body, flags=re.S)
        for target in re.findall(r"(?<!!)\[[^\]\n]*\]\(([^\s)]+)\)", body):
            target = target.strip("<>")
            if target.startswith(("http:", "https:", "plugin:", "codex:", "#")):
                continue
            file_part = unquote(target.split("#", 1)[0])
            resolved = Path(file_part)
            if not resolved.is_absolute():
                resolved = (path.parent / resolved).resolve()
            require(resolved.is_file() or resolved.is_dir(), f"Broken current handoff link: {path}: {target}")
            links.append({"from": str(path), "target": target, "resolved": str(resolved)})
    evidence = read(case / "evidence-records.json")
    require(len(evidence["evidence"]) == len(index["artifacts"]) == 13, "Incomplete primary Evidence records")
    for row in evidence["evidence"]:
        require(row["source_type"] == "file" and row["repro_command"] and
                digest(row["source_ref"]) == row["content_hash"], "Invalid primary Evidence source")
    proof = {"kind": "current-stock-source-handoff-integrity", "passed": True,
             "integrationUniqueFiles": len(index["verifiedFiles"]), "files": hashes,
             "currentMarkdownLinks": len(links), "links": links, "primaryEvidenceRecords": 13,
             "gameExecuted": False, "installed": False, "fullB01B19Completed": False}
    (output / "handoff-integrity.json").write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: proof[key] for key in ("passed", "integrationUniqueFiles", "currentMarkdownLinks", "primaryEvidenceRecords")},
                     ensure_ascii=False))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    main(parser.parse_args().output)
