"""End-to-end check of the WinASM65 debug adapter against a real Mesen2.

Usage:
    python tools/dap-smoke.py <adapter.exe|adapter.dll> <config.json> <source> <line>

Launches the project through the adapter exactly as VS Code would, sets one
breakpoint at <source>:<line>, and checks that the machine stops there, that
registers and memory read, that "next" moves on, and that disconnecting ends the
emulator. Close any running Mesen2 first (single instance).
"""
import json
import os
import queue
import subprocess
import sys
import threading
import time


def main():
    if len(sys.argv) != 5:
        sys.exit(__doc__)
    adapter, config, source, line = sys.argv[1], os.path.abspath(sys.argv[2]), os.path.abspath(sys.argv[3]), int(sys.argv[4])
    command = ["dotnet", adapter, "--dap"] if adapter.endswith(".dll") else [adapter, "--dap"]
    proc = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=sys.stderr)
    messages = queue.Queue()
    failures = []
    seq = [0]
    backlog = []

    def reader():
        out = proc.stdout
        while True:
            header = b""
            while not header.endswith(b"\r\n\r\n"):
                b = out.read(1)
                if not b:
                    return
                header += b
            length = int([h for h in header.decode().split("\r\n") if h.lower().startswith("content-length")][0].split(":")[1])
            messages.put(json.loads(out.read(length)))
    threading.Thread(target=reader, daemon=True).start()

    def take(match, timeout=20):
        for i, m in enumerate(backlog):
            if match(m):
                return backlog.pop(i)
        end = time.time() + timeout
        while time.time() < end:
            try:
                m = messages.get(timeout=max(0.01, end - time.time()))
            except queue.Empty:
                break
            if m.get("type") == "event" and m.get("event") == "output":
                print("   | " + m["body"]["output"].rstrip())
            if match(m):
                return m
            backlog.append(m)
        return None

    def request(command, arguments=None, timeout=30):
        seq[0] += 1
        body = json.dumps({"seq": seq[0], "type": "request", "command": command, "arguments": arguments or {}}).encode()
        proc.stdin.write(b"Content-Length: %d\r\n\r\n" % len(body) + body)
        proc.stdin.flush()
        n = seq[0]
        return take(lambda m: m.get("type") == "response" and m.get("request_seq") == n, timeout)

    def event(name, timeout=20):
        return take(lambda m: m.get("type") == "event" and m.get("event") == name, timeout)

    def check(ok, what):
        print(("PASS " if ok else "FAIL ") + what, flush=True)
        if not ok:
            failures.append(what)

    try:
        check(request("initialize", {"adapterID": "winasm65"})["success"], "initialize")
        r = request("launch", {"config": config}, timeout=60)
        check(r is not None and r["success"], "launch: %s" % (r and r.get("message")))
        if not r or not r["success"]:
            return
        check(event("initialized") is not None, "initialized event")

        r = request("setBreakpoints", {"source": {"path": source}, "breakpoints": [{"line": line}]})
        bp = r["body"]["breakpoints"][0]
        check(bp["verified"], "breakpoint verified at line %d" % bp["line"])
        request("configurationDone")

        stopped = event("stopped")
        check(stopped is not None and stopped["body"]["reason"] == "breakpoint", "stopped at the breakpoint")
        frame = request("stackTrace", {"threadId": 1})["body"]["stackFrames"][0]
        check(frame.get("line") == bp["line"] and os.path.samefile(frame["source"]["path"], source),
              "frame %s at %s:%s" % (frame["name"], os.path.basename(frame.get("source", {}).get("path", "?")), frame.get("line")))
        regs = {v["name"]: v["value"] for v in request("variables", {"variablesReference": 1})["body"]["variables"]}
        print("   registers:", regs)
        check(regs["PC"] == frame["instructionPointerReference"].replace("0x", "$"), "PC matches the frame")

        request("next", {"threadId": 1})
        stopped = event("stopped")
        check(stopped is not None and stopped["body"]["reason"] == "step", "next stops on the following line")
        frame2 = request("stackTrace", {"threadId": 1})["body"]["stackFrames"][0]
        check(frame2.get("line", 0) > frame["line"], "now at line %s" % frame2.get("line"))

        mem = request("readMemory", {"memoryReference": "0x0000", "count": 0x800})
        import base64
        ram = base64.b64decode(mem["body"]["data"]) if mem["success"] else b""
        check(len(ram) == 0x800, "readMemory of all RAM: %d bytes" % len(ram))
        symbols = request("winasm65.symbols", {"from": 0, "to": 0x7FF})
        names = [s["name"] for s in symbols["body"]["symbols"]] if symbols["success"] else []
        check(len(names) > 0, "RAM symbols: %d, e.g. %s" % (len(names), ", ".join(names[:5])))
        cpu = request("evaluate", {"expression": "CPU", "context": "repl"})
        check(cpu["success"] and "PC=$" in cpu["body"]["result"], "debug console CPU: " + cpu["body"]["result"].splitlines()[0])

        request("continue", {"threadId": 1})
        stopped = event("stopped")
        check(stopped is not None and stopped["body"]["reason"] == "breakpoint", "continue runs to the breakpoint again")

        request("setBreakpoints", {"source": {"path": source}, "breakpoints": []})
        request("continue", {"threadId": 1})
        check(event("stopped", timeout=2) is None, "without breakpoints the machine keeps running")
        request("pause", {"threadId": 1})
        stopped = event("stopped")
        check(stopped is not None and stopped["body"]["reason"] == "pause", "pause stops")

        r = request("disconnect", {"terminateDebuggee": True})
        check(r is not None and r["success"], "disconnect")
    finally:
        try:
            proc.stdin.close()
        except OSError:
            pass
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
