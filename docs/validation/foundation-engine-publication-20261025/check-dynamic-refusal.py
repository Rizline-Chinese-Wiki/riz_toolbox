"""Check fixed Bootstrap dynamic-load refusal in two self-owned Python children.

Root runs: python check-dynamic-refusal.py --build ABS/publication-build-manifest.json
    --output ABS/new-directory

Only the owned and production-candidate Bootstrap from that native build can be
selected. Each case contains four fixed images; only Bootstrap and its ordinary
Kernel dependency are loaded. No owned target, game image or target API is run.
"""
from __future__ import annotations

import argparse
import ctypes as C
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys


BOOTSTRAP = "RizToolbox.EngineBootstrap.dll"
KERNEL = "RizToolbox.EngineNodeKernel.dll"
TARGET = "RizEngineOwnedTargets.dll"
EXECUTABLE = "EnginePublication.Host.exe"
NAMES = (BOOTSTRAP, KERNEL, TARGET, EXECUTABLE)
PROFILES = {"owned": 1, "production-candidate": 2}
KERNEL_SHA = "923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662"
LOAD_FLAGS = 0x00000100 | 0x00000800
U32, U64, PTR = C.c_uint32, C.c_uint64, C.c_void_p


def require(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def save(path, value):
    with path.open("x", encoding="utf-8", newline="\n") as output:
        json.dump(value, output, ensure_ascii=False, indent=2)
        output.write("\n")


def absolute(value):
    path = Path(value)
    require(path.is_absolute(), "An absolute path is required")
    return path.resolve()


def digest(value):
    require(re.fullmatch(r"[0-9a-f]{64}", value) is not None, "Expected SHA256 must be lowercase hex")
    return value


def normalized(value):
    text = str(value)
    if text.startswith("\\\\?\\"):
        text = text[4:]
    return str(Path(text).resolve()).replace("\\", "/").casefold()


class FileInfo(C.Structure):
    _fields_ = [("attributes", U32), ("creation", U32 * 2), ("access", U32 * 2), ("write", U32 * 2),
                ("volume", U32), ("sizeHigh", U32), ("sizeLow", U32), ("links", U32), ("indexHigh", U32), ("indexLow", U32)]


class Win:
    def __init__(self):
        require(os.name == "nt" and C.sizeof(PTR) == 8, "This check requires native Windows x64 Python")
        # The tests call the actual Win32 SetLastError/GetLastError directly.
        # ctypes' private LastError swapping would otherwise hide changes made
        # by the separately constructed native-export CFUNCTYPE calls.
        self.dll = C.WinDLL("kernel32.dll", use_last_error=False)
        for name, result, arguments in (
            ("CreateFileW", PTR, (C.c_wchar_p, U32, U32, PTR, U32, U32, PTR)),
            ("GetFileInformationByHandle", C.c_int, (PTR, C.POINTER(FileInfo))),
            ("ReadFile", C.c_int, (PTR, PTR, U32, C.POINTER(U32), PTR)),
            ("SetFilePointerEx", C.c_int, (PTR, C.c_int64, PTR, U32)),
            ("CloseHandle", C.c_int, (PTR,)),
            ("LoadLibraryExW", PTR, (C.c_wchar_p, PTR, U32)),
            ("GetModuleHandleW", PTR, (C.c_wchar_p,)),
            ("GetModuleFileNameW", U32, (PTR, C.c_wchar_p, U32)),
            ("GetProcAddress", PTR, (PTR, C.c_char_p)),
            ("GetCurrentThreadId", U32, ()),
            ("GetLastError", U32, ()),
            ("SetLastError", None, (U32,)),
        ):
            function = getattr(self.dll, name)
            function.restype, function.argtypes = result, arguments
            setattr(self, name, function)

    def identity(self, handle):
        info = FileInfo()
        require(self.GetFileInformationByHandle(handle, C.byref(info)), "Actual file identity query failed")
        return dict(volumeSerial=info.volume, fileIndex=(info.indexHigh << 32) | info.indexLow,
                    bytes=(info.sizeHigh << 32) | info.sizeLow)

    def file_sha(self, handle):
        require(self.SetFilePointerEx(handle, 0, None, 0), "Cannot rewind held input")
        hasher = hashlib.sha256()
        buffer, count = C.create_string_buffer(65536), U32()
        while True:
            require(self.ReadFile(handle, buffer, len(buffer), C.byref(count), None), "Cannot hash held input")
            if not count.value:
                break
            hasher.update(buffer.raw[:count.value])
        return hasher.hexdigest()

    def module_path(self, module):
        buffer = C.create_unicode_buffer(32768)
        count = self.GetModuleFileNameW(module, buffer, len(buffer))
        require(0 < count < len(buffer), "Loaded module path is unavailable or truncated")
        return buffer.value


class Held:
    def __init__(self, win, path, expected):
        self.win, self.path = win, path
        self.handle = win.CreateFileW(str(path), 0x80000000, 1, None, 3, 0x80, None)
        require(self.handle not in (None, C.c_void_p(-1).value), "Cannot hold input against write/delete: " + str(path))
        try:
            self.before = dict(path=str(path), sha256=win.file_sha(self.handle), identity=win.identity(self.handle),
                               writeAndDeleteDenied=True)
            require(self.before["sha256"] == expected, "Held input hash differs: " + str(path))
        except BaseException:
            win.CloseHandle(self.handle)
            self.handle = None
            raise

    def finish(self):
        after_sha, after_identity = self.win.file_sha(self.handle), self.win.identity(self.handle)
        require(after_sha == self.before["sha256"] and after_identity == self.before["identity"], "Held input changed")
        # Also check the named path still identifies the held file.
        named = self.win.CreateFileW(str(self.path), 0x80000000, 1, None, 3, 0x80, None)
        require(named not in (None, C.c_void_p(-1).value), "Held input path disappeared")
        try:
            require(self.win.identity(named) == after_identity, "Named input was replaced")
        finally:
            self.win.CloseHandle(named)
        return dict(**self.before, afterSha256=after_sha, afterIdentity=after_identity)

    def close(self):
        if self.handle is not None:
            self.win.CloseHandle(self.handle)
            self.handle = None


def decode_v4(raw):
    require(len(raw) == 4096, "V4 record must have 4096 raw bytes")
    fields = dict(zip(("version", "byteSize", "state", "failure"), struct.unpack_from("<4I", raw)))
    fields["epoch"], = struct.unpack_from("<Q", raw, 16)
    fields.update(zip(("ownerThread", "processId", "attachReason", "startupStatic", "claimed", "profile",
                       "configuredMask", "boundMask", "committedMask", "sourceWriteAttempts", "sourceWriteCompletions",
                       "protectionOutstanding", "eventCount", "overflow"), struct.unpack_from("<14I", raw, 24)))
    fields.update(zip(("reservedPointer", "bootstrapModule", "kernelModule", "targetModule", "executableModule"),
                      struct.unpack_from("<5Q", raw, 80)))
    fields["admissionFlags"], fields["verifyFailures"] = struct.unpack_from("<2I", raw, 120)
    fields["events"] = [dict(zip(("sequence", "stage", "kind", "state", "failure", "win32Error", "reserved"),
                                struct.unpack_from("<Q6I", raw, 1792 + index * 32))) for index in range(fields["eventCount"])]
    return fields


def check_refused(raw, profile, pid, unknown):
    record = decode_v4(raw)
    require((record["version"], record["byteSize"], record["state"], record["failure"])
            == (4, 4096, 6 if unknown else 5, 1), "Dynamic load refusal state/failure differs")
    require(record["processId"] == pid and record["attachReason"] == 1 and record["ownerThread"] != 0
            and record["profile"] == profile and record["startupStatic"] == 0 and record["reservedPointer"] == 0
            and record["claimed"] == 1, "Actual dynamic DllEntry identity differs")
    require(all(record[key] == 0 for key in ("epoch", "configuredMask", "boundMask", "committedMask", "sourceWriteAttempts",
                "sourceWriteCompletions", "protectionOutstanding", "admissionFlags", "overflow", "bootstrapModule",
                "kernelModule", "targetModule", "executableModule")), "Refused component acquired a publication responsibility")
    require(not any(raw[128:1792]) and not any(raw[3840:]), "No module/target descriptors may be configured on dynamic refusal")
    require(record["verifyFailures"] == (1 if unknown else 0) and record["eventCount"] == (3 if unknown else 2),
            "Refusal/Verify event accounting differs")
    require([event["stage"] for event in record["events"]] == ([1, 17, 17] if unknown else [1, 17]),
            "Actual dynamic refusal event sequence differs")
    require(not any(raw[1792 + record["eventCount"] * 32:3840]), "Unused events must remain zero")
    return record


def child(args):
    case = absolute(args.case_dir)
    require(case.name in PROFILES and args.profile == case.name, "Fixed case directory/profile required")
    expected = {BOOTSTRAP: digest(args.bootstrap_sha), KERNEL: digest(args.kernel_sha),
                TARGET: digest(args.target_sha), EXECUTABLE: digest(args.executable_sha)}
    require(expected[KERNEL] == KERNEL_SHA, "Only the independently verified fixed Kernel is accepted")
    require(not (case / "child-result.json").exists(), "Preserve earlier dynamic child evidence")
    win, held, calls = Win(), [], []
    report = dict(kind="engine-bootstrap-dynamic-refusal-child", passed=False, actualProcessId=os.getpid(),
                  profile=args.profile, loadFlags=LOAD_FLAGS, nativeExportCalls=calls, gameExecuted=False, installed=False,
                  targetApisCalled=False, configureCalled=False, error=None)
    try:
        for name in NAMES:
            held.append(Held(win, case / name, expected[name]))
        require(not win.GetModuleHandleW(BOOTSTRAP) and not win.GetModuleHandleW(KERNEL)
                and not win.GetModuleHandleW(TARGET), "Fixed images must start unloaded in this fresh child")
        module = win.LoadLibraryExW(str(case / BOOTSTRAP), None, LOAD_FLAGS)
        require(module, "Absolute dynamic Bootstrap load failed: " + str(win.GetLastError()))
        kernel = win.GetModuleHandleW(KERNEL)
        require(kernel and not win.GetModuleHandleW(TARGET), "Only the ordinary Kernel dependency may be loaded")
        require(normalized(win.module_path(module)) == normalized(case / BOOTSTRAP)
                and normalized(win.module_path(kernel)) == normalized(case / KERNEL), "Actual loaded paths differ from fixed deployed inputs")
        report["moduleHandles"] = dict(bootstrap=module, kernel=kernel, target=0)
        report["loadedPaths"] = dict(bootstrap=win.module_path(module), kernel=win.module_path(kernel))

        def export(owner, name, result, *arguments):
            address = win.GetProcAddress(owner, name.encode("ascii"))
            require(address, "Fixed native export is absent: " + name)
            return C.CFUNCTYPE(result, *arguments)(address), address

        def scalar(owner, name):
            invoke, address = export(owner, name, U32)
            seed = 0x69e40000 + len(calls)
            win.SetLastError(seed)
            result = int(invoke())
            calls.append(dict(name=name, address=address, result=result, lastErrorBefore=seed, lastErrorAfter=win.GetLastError()))
            return result

        require(scalar(module, "RizEngineFirstInstall_GetVersion") == 4, "Bootstrap ABI version differs")
        for name, value in (("GetAbiVersion", 2), ("GetLimit", 32), ("GetFrameSize", 112), ("GetCompletionSize", 32)):
            require(scalar(kernel, "RizEngineNodeContext_" + name) == value, "Actual Kernel ABI export differs")
        read, read_address = export(module, "RizEngineFirstInstall_GetInfoV4", U32, PTR)
        verify, verify_address = export(module, "RizEngineFirstInstall_VerifyV4", U32)
        kernel_info, kernel_info_address = export(kernel, "RizEngineNodeContext_GetInfo", U32, U32, U32, PTR)
        kernel_entry, kernel_entry_address = export(kernel, "RizEngineNodeContext_GetEntry", PTR, U32, U32)

        def snapshot(label, unknown):
            data = C.create_string_buffer(struct.pack("<2I", 4, 4096) + bytes(4088), 4096)
            seed = 0x69e40000 + len(calls)
            win.SetLastError(seed)
            result = int(read(data))
            after = win.GetLastError()
            calls.append(dict(name="RizEngineFirstInstall_GetInfoV4", address=read_address, result=result,
                              lastErrorBefore=seed, lastErrorAfter=after, snapshot=label + ".v4.bin"))
            require(result == 0 and after == seed, "GetInfo refusal snapshot failed or changed LastError")
            raw = data.raw
            decoded = check_refused(raw, PROFILES[args.profile], os.getpid(), unknown)
            path = case / (label + ".v4.bin")
            with path.open("xb") as output:
                output.write(raw)
            return dict(path=path.name, sha256=sha(raw), byteSize=len(raw), record=decoded)

        def kernel_unconfigured(label):
            records = []
            for kind in range(4):
                data = C.create_string_buffer(struct.pack("<2I", 1, 112) + bytes(104), 112)
                result = int(kernel_info(kind, 28, data))
                entry = int(kernel_entry(kind, 28) or 0)
                raw = data.raw
                header, pointers = struct.unpack_from("<6I", raw), struct.unpack_from("<3Q", raw, 24)
                require(result == 2 and header == (1, 112, kind, 28, 0, 0) and pointers == (0, 0, entry)
                        and entry != 0 and not any(raw[48:]), "Actual Kernel lane must remain unconfigured and fault-free")
                filename = label + ".kernel-" + str(kind) + ".bin"
                with (case / filename).open("xb") as output:
                    output.write(raw)
                calls.append(dict(name="RizEngineNodeContext_GetInfo", address=kernel_info_address, args=[kind, 28], result=result,
                                  snapshot=filename, byteSize=112, sha256=sha(raw)))
                calls.append(dict(name="RizEngineNodeContext_GetEntry", address=kernel_entry_address, args=[kind, 28], result=entry))
                records.append(dict(kind=kind, slot=28, result=result, entry=entry, path=filename, byteSize=112, sha256=sha(raw)))
            require(not win.GetModuleHandleW(TARGET), "Refusal check must never load owned target")
            return records

        report["before"] = snapshot("before-verify", False)
        report["kernelBefore"] = kernel_unconfigured("before-verify")
        seed = 0x69e40000 + len(calls)
        win.SetLastError(seed)
        result = int(verify())
        after = win.GetLastError()
        calls.append(dict(name="RizEngineFirstInstall_VerifyV4", address=verify_address, result=result,
                          lastErrorBefore=seed, lastErrorAfter=after))
        require(result == 1 and after == seed, "Verify must refuse dynamic installation and preserve LastError")
        report["after"] = snapshot("after-verify", True)
        report["kernelAfter"] = kernel_unconfigured("after-verify")
        repeated = win.LoadLibraryExW(str(case / BOOTSTRAP), None, LOAD_FLAGS)
        require(repeated == module, "Repeated LoadLibrary must retain the same dynamic module")
        report["repeatLoadHandle"] = repeated
        report["afterRepeat"] = snapshot("after-repeat-load", True)
        require(report["after"]["sha256"] == report["afterRepeat"]["sha256"], "Repeated load revived or rewrote Unknown")
        report["kernelAfterRepeat"] = kernel_unconfigured("after-repeat-load")
        report["heldInputs"] = [item.finish() for item in held]
        report["passed"] = True
    except BaseException as error:
        report["error"] = type(error).__name__ + ": " + str(error)
    finally:
        # HMODULE references deliberately survive until process termination.
        # Only file handles are closed; no FreeLibrary, target or Configure call.
        for item in held:
            item.close()
        save(case / "child-result.json", report)
    return 0 if report["passed"] else 1


def parent(args):
    build, output = absolute(args.build), absolute(args.output)
    require(build.name == "publication-build-manifest.json" and build.is_file(), "Exact native build manifest required")
    require(not output.exists(), "Output must be a new evidence directory")
    win, held = Win(), []
    manifest = json.loads(build.read_text(encoding="utf-8-sig"))
    require(manifest.get("kind") == "engine-publication-build" and manifest.get("schemaVersion") == 1,
            "Unsupported native build manifest")
    plan = {}
    for profile in PROFILES:
        selected = [row for row in manifest["files"] if row.get("profile") == profile]
        require(len(selected) == 1 and selected[0].get("stage") == 0 and selected[0].get("index") == 0,
                "Only the fixed nonfault Bootstrap profiles can be selected")
        mapping = {BOOTSTRAP: selected[0]}
        for name in NAMES[1:]:
            rows = [row for row in manifest["files"] if row.get("profile") == "shared" and Path(row["path"]).name == name]
            require(len(rows) == 1, "Manifest lacks one exact shared binary: " + name)
            mapping[name] = rows[0]
        for name, row in mapping.items():
            path = absolute(row["path"])
            require(path.name == name and path.is_relative_to(build.parent), "Fixed binary leaves native build directory")
            row["sha256"] = digest(row["sha256"])
        require(mapping[KERNEL]["sha256"] == KERNEL_SHA, "Native build Kernel differs from independently verified core")
        plan[profile] = mapping
    output.mkdir(parents=True)
    result = dict(kind="engine-bootstrap-dynamic-refusal-parent", schemaVersion=1, passed=False,
                  actualParentPid=os.getpid(), build=str(build), buildManifestSha256=sha(build.read_bytes()),
                  cases=[], gameExecuted=False, installed=False, productionAdmissionGranted=False,
                  targetImagesLoaded=False, completeFaultMatrix=False, error=None)
    try:
        for path, expected in ((build, result["buildManifestSha256"]), (Path(__file__).resolve(), sha(Path(__file__).read_bytes())),
                               (Path(sys.executable).resolve(), sha(Path(sys.executable).read_bytes()))):
            held.append(Held(win, path, expected))
        for profile, mapping in plan.items():
            case = output / profile
            case.mkdir()
            for name, row in mapping.items():
                source = absolute(row["path"])
                held.append(Held(win, source, row["sha256"]))
                shutil.copyfile(source, case / name)
                held.append(Held(win, case / name, row["sha256"]))
            command = [str(Path(sys.executable).resolve()), str(Path(__file__).resolve()), "--child", "--case-dir", str(case),
                       "--profile", profile, "--bootstrap-sha", mapping[BOOTSTRAP]["sha256"],
                       "--kernel-sha", mapping[KERNEL]["sha256"], "--target-sha", mapping[TARGET]["sha256"],
                       "--executable-sha", mapping[EXECUTABLE]["sha256"]]
            item = dict(profile=profile, command=command, actualChildPid=None, actualExitCode=None, timedOut=False)
            result["cases"].append(item)
            with (case / "stdout.log").open("xb") as stdout, (case / "stderr.log").open("xb") as stderr:
                process = subprocess.Popen(command, cwd=case, stdin=subprocess.DEVNULL, stdout=stdout, stderr=stderr,
                                           creationflags=0x08000000)
                item["actualChildPid"] = process.pid
                try:
                    item["actualExitCode"] = process.wait(timeout=30)
                except subprocess.TimeoutExpired:
                    item["timedOut"] = True
                    process.kill()
                    item["actualExitCode"] = process.wait(timeout=10)
            report_path = case / "child-result.json"
            require(not item["timedOut"] and item["actualExitCode"] == 0 and report_path.is_file(), "Dynamic child did not finish successfully")
            report = json.loads(report_path.read_text(encoding="utf-8"))
            require(report["passed"] and report["actualProcessId"] == process.pid and report["profile"] == profile,
                    "Child report does not identify the actual spawned process")
            require(report["loadFlags"] == LOAD_FLAGS and report["repeatLoadHandle"] == report["moduleHandles"]["bootstrap"]
                    and report["moduleHandles"]["target"] == 0 and not report["targetApisCalled"] and not report["configureCalled"],
                    "Child loaded an unexpected image or gained publication authority")
            require(report["after"]["sha256"] == report["afterRepeat"]["sha256"], "Unknown changed after repeated load")
            for name, unknown in (("before", False), ("after", True), ("afterRepeat", True)):
                capture = report[name]
                raw = (case / capture["path"]).read_bytes()
                require(sha(raw) == capture["sha256"] and check_refused(raw, PROFILES[profile], process.pid, unknown) == capture["record"],
                        "Parent raw V4 verification differs from child report")
            for phase in ("kernelBefore", "kernelAfter", "kernelAfterRepeat"):
                require(len(report[phase]) == 4, "Four actual Kernel snapshots required per phase")
                for row in report[phase]:
                    raw = (case / row["path"]).read_bytes()
                    require(len(raw) == 112 and sha(raw) == row["sha256"] and row["result"] == 2
                            and struct.unpack_from("<6I", raw) == (1, 112, row["kind"], 28, 0, 0)
                            and struct.unpack_from("<3Q", raw, 24) == (0, 0, row["entry"]) and not any(raw[48:]),
                            "Parent raw Kernel refusal verification differs")
            item.update(passed=True, childReportSha256=sha(report_path.read_bytes()),
                        snapshots=[report[key]["path"] for key in ("before", "after", "afterRepeat")],
                        nativeExportCallCount=len(report["nativeExportCalls"]), targetHandle=report["moduleHandles"]["target"])
        result["heldInputs"] = [item.finish() for item in held]
        result["passed"] = True
    except BaseException as error:
        result["error"] = type(error).__name__ + ": " + str(error)
    finally:
        for item in held:
            item.close()
        save(output / "dynamic-refusal-result.json", result)
    print(json.dumps({key: result[key] for key in ("passed", "actualParentPid", "error")}))
    return 0 if result["passed"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build")
    parser.add_argument("--output")
    parser.add_argument("--child", action="store_true", help=argparse.SUPPRESS)
    for name in ("case-dir", "profile", "bootstrap-sha", "kernel-sha", "target-sha", "executable-sha"):
        parser.add_argument("--" + name, help=argparse.SUPPRESS)
    args = parser.parse_args()
    child_values = (args.case_dir, args.profile, args.bootstrap_sha, args.kernel_sha, args.target_sha, args.executable_sha)
    if args.child:
        require(all(child_values) and args.profile in PROFILES and not args.build and not args.output,
                "Only the fixed internal child invocation is accepted")
        return child(args)
    require(args.build and args.output and not any(child_values), "Use --build and --output only")
    return parent(args)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(type(error).__name__ + ": " + str(error), file=sys.stderr)
        raise SystemExit(1)
