"""Create and verify a complete report index without changing reviewed inputs."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def record(path):
    return {"path": str(path.resolve()), "bytes": path.stat().st_size, "sha256": digest(path)}


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def verify(index):
    rows = index["reportFiles"] + index["reviewedSources"] + index["verificationInputs"] + [
        index["evidenceReview"], index["reviewSourceArchive"], index["nativeSourceArchive"]]
    rows += [row for directory in index["externalRuns"] for row in directory["files"]]
    for row in rows:
        assert digest(Path(row["path"])) == row["sha256"], row["path"]
    return len(rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--review", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--verify-only", action="store_true")
    args = parser.parse_args()
    assert args.output.is_absolute() and not args.output.exists(), "Use a fresh absolute output directory"
    args.output.mkdir(parents=True)
    case = Path(__file__).resolve().parent
    index_path = case / "evidence-index.json"
    review = read(args.review)
    assert review["passed"] and not review["actualProcessExecutionByThisReview"]
    if not args.verify_only:
        local = case / "evidence"
        assert not index_path.exists() and not local.exists(), "Do not overwrite a sealed report"
        local.mkdir()
        definitions = [
            ("E-NATIVE", "当前native构建与源归档", Path(review["nativeManifest"]["path"]), "native-manifest.json"),
            ("E-MAPPED", "七个实际副本映像进程", Path(review["mappedResult"]["path"]), "mapped-r4-test-result.json"),
            ("E-MACHINE", "自有真实单步与原body SEH", Path(review["machineResult"]["path"]), "machine-r4-test-result.json"),
            ("E-RELOCATED", "normalized original内部真实AV", Path(review["relocatedResult"]["path"]), "relocated-seh-r1-test-result.json"),
            ("E-REVIEW", "独立离线PE/PDB/COFF/源审查", args.review, "offline-review.json"),
            ("E-FAIL-MACHINE", "保留的TF单步覆盖失败", Path("D:/riz-build-cache-next/foundation-private-machine-r1-20261031/test-result.json"), "machine-r1-failure.json"),
            ("E-FAIL-MAPPED", "保留的fixture编译失败", Path("D:/riz-build-cache-next/foundation-private-mapped-r1-20261031/build.log"), "mapped-r1-build-failure.log")]
        evidence = []
        for identity, title, source, name in definitions:
            destination = local / name
            shutil.copyfile(source, destination)
            assert digest(source) == digest(destination)
            evidence.append({"id": identity, "title": title, "observed_at": review["observedAtUtc"],
                             "source_type": "file", "source_ref": str(source.resolve()), "content_hash": digest(source),
                             "artifact_path": destination.relative_to(case).as_posix(),
                             "repro_command": "& 'C:/Python314/python.exe' -B './docs/validation/foundation-private-attestation-20261031/Verify-Evidence.py' --output 'D:/riz-build-cache-next/foundation-private-proof-reproduce-20261031'",
                             "raw_excerpt": (read(source).get("kind", "result") if source.suffix == ".json" else "C3536/C2440/C2664; no persisted standalone build exit field"),
                             "linked_workitem": "n/a", "supersedes": "none"})
        findings = [
            {"id": "F-OWNER", "title": "真实私有pre-Prepare发行路径", "severity": "n/a_re", "category": "other", "status": "validated", "confidence": "high",
             "evidence_ids": ["E-NATIVE", "E-MAPPED", "E-REVIEW"], "location": "native/RizToolbox.JudgeContext/FirstInstall.cpp::Attempt",
             "impact": "28实际original及64实际write由首个Prepare前原文件/精确DIR64基线派生，无caller mask发行路径。", "repro_steps": ["读取当前native构建及mapped七模式结果。", "运行离线Verify-Evidence并核对应源/PDB/COFF。"], "remediation": "n/a; no frozen-source change found necessary", "optional_attack": ""},
            {"id": "F-ORIGINAL", "title": "raw保留、独立RX与完整展开证据", "severity": "n/a_re", "category": "other", "status": "validated", "confidence": "high",
             "evidence_ids": ["E-MACHINE", "E-RELOCATED", "E-REVIEW"], "location": "native/RizToolbox.JudgeContext/FirstInstallPlaybackAttestation.inl::Prove/VerifyOriginal",
             "impact": "28有限前缀闭集通过真实单步与body SEH，另有normalized original内部+4实际AV展开；不推广到全部真实游戏正文。", "repro_steps": ["核machine/relocated的actualExit及stdout。", "核PDB模块与实际COFF的完整C13校验值。"], "remediation": "n/a", "optional_attack": ""},
            {"id": "F-IDENTITY", "title": "实际编译符号与归档源闭合", "severity": "n/a_re", "category": "other", "status": "validated", "confidence": "high",
             "evidence_ids": ["E-NATIVE", "E-REVIEW"], "location": "native-r3 / core-r3 exact source hashes",
             "impact": "5 PE/PDB、31 COFF、73 PDB模块绑定、101 native归档源；9相关managed源与core-r3副本一致。", "repro_steps": ["执行Verify-Evidence。", "逐一核evidence-index全部SHA。"], "remediation": "n/a", "optional_attack": ""},
            {"id": "F-COMPOSITION", "title": "真实CLR membership组合静态路径", "severity": "n/a_re", "category": "design", "status": "candidate", "confidence": "high",
             "evidence_ids": ["E-REVIEW", "E-MAPPED"], "location": "NativeFirstInstall.PlaybackMembership.cs / ExactHookBroker.Playback.Group.cs / NativePlaybackExecutableSeal*.cs",
             "impact": "实际owner与唯一membership引用、native身份交叉核及before==pinned file+DIR64后compose after已静态检查；本项不替代共同组整合执行。", "repro_steps": ["核9项相关managed源与core-r3同hash。", "沿报告P-ISSUE第4步检查组合路径。"], "remediation": "由父任务单独报告完整共同组执行结果。", "optional_attack": ""}]
        path = {"id": "P-ISSUE", "title": "实际FirstInstall到CLR membership的发行与组合", "path_type": "callflow",
                "start": "FirstInstall::Attempt before first DobbyPrepare", "goal": "current opaque CLR membership",
                "steps": [{"action": "固定文件与精确DIR64原映像基线", "evidence_ids": ["E-NATIVE", "E-MAPPED", "E-REVIEW"], "finding_ids": ["F-OWNER"]},
                          {"action": "28 raw Dobby -> 独立RX -> RF/UW -> actual native lane", "evidence_ids": ["E-MAPPED", "E-MACHINE", "E-RELOCATED"], "finding_ids": ["F-ORIGINAL"]},
                          {"action": "64 actual writes -> Publish -> readonly Get/Read", "evidence_ids": ["E-NATIVE", "E-MAPPED"], "finding_ids": ["F-OWNER"]},
                          {"action": "actual CLR owner -> unique membership -> pinned-file/DIR64 composition", "evidence_ids": ["E-REVIEW", "E-MAPPED"], "finding_ids": ["F-COMPOSITION"]}],
                "residual_risks": ["共同组实际运行由父任务单独验证。", "游戏原方法/init/DllMain调用为0。", "没有当前候选重包/安装/production准入结论。"]}
        write(case / "evidence-records.json", {"schemaVersion": 1, "flavor": None, "scope": "scope.md", "timeline": "timeline.md", "evidence": evidence, "findings": findings, "paths": [path]})
        files = [record(path) for path in sorted(case.rglob("*")) if path.is_file() and path != index_path]
        index = {"schemaVersion": 1, "kind": "foundation-private-attestation-complete-hash-index", "flavor": None,
                 "observedAtUtc": datetime.now(timezone.utc).isoformat(),
                 "claimBoundaries": {"gameMethodsInvoked": 0, "gameDllMainInvoked": 0, "originalIl2CppInitInvoked": 0,
                                     "productionAdmission": False, "candidatePackageVerified": False, "installed": False,
                                     "coreR3OnlyRelevantSourceHashesReviewedHere": True},
                 "evidenceReview": record(args.review), "nativeSourceArchive": review["nativeArchive"],
                 "reviewSourceArchive": review["reviewSourceArchive"], "reviewedSources": review["reviewSources"],
                 "verificationInputs": review["verificationInputs"], "externalRuns": review["directories"], "reportFiles": files,
                 "evidenceRecords": "evidence-records.json", "indexSelfHashPolicy": "Index is excluded from its own reportFiles; its SHA is recorded in the fresh external seal result."}
        write(index_path, index)
    index = read(index_path)
    count = verify(index)
    evidence = read(case / "evidence-records.json")
    ids = {row["id"] for row in evidence["evidence"]}
    for row in evidence["evidence"]:
        assert digest(case / row["artifact_path"]) == row["content_hash"]
    for row in evidence["findings"]:
        assert row["evidence_ids"] and set(row["evidence_ids"]) <= ids
        if row["status"] == "validated":
            assert len(row["evidence_ids"]) >= 2 and row["confidence"] != "low"
    result = {"schemaVersion": 1, "kind": "private-attestation-report-integrity", "passed": True,
              "observedAtUtc": datetime.now(timezone.utc).isoformat(), "index": record(index_path),
              "checkedHashRows": count, "externalDirectories": len(index["externalRuns"]),
              "externalFiles": sum(len(row["files"]) for row in index["externalRuns"]),
              "evidence": len(evidence["evidence"]), "findings": len(evidence["findings"]), "paths": len(evidence["paths"]),
              "gameExecuted": False, "installed": False, "productionAdmission": False, "candidatePackageVerified": False}
    write(args.output / "report-integrity.json", result)
    print(json.dumps({"passed": True, "indexSha256": digest(index_path), "checkedHashRows": count,
                      "output": str(args.output / "report-integrity.json")}))


if __name__ == "__main__":
    main()
