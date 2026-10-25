"""End-to-end check of the MCP server, as an AI assistant would use it.

Usage:
    python tools/mcp-smoke.py <WinASM65.Tooling(.exe|.dll)> <config.json> <source> <line> [<Mesen.exe>]

Starts `WinASM65.Tooling --mcp`, speaks MCP over stdio (one JSON message per
line), and checks: initialize, tools/list, build_project, get_listing of the
breakpoint line, describe_instruction; then, with an emulator (the project's
Emulator section, or the optional Mesen.exe), debug_launch with a breakpoint on
<source>:<line>, the stop, get_state, read_memory of the PAL space, and
debug_stop. Close any running Mesen2 first: it runs as a single instance.
"""
import json
import os
import subprocess
import sys


def main():
    if len(sys.argv) not in (5, 6):
        sys.exit(__doc__)
    tooling, config, source, line = sys.argv[1], os.path.abspath(sys.argv[2]), os.path.abspath(sys.argv[3]), int(sys.argv[4])
    emulator = sys.argv[5] if len(sys.argv) == 6 else None
    command = ["dotnet", tooling, "--mcp"] if tooling.endswith(".dll") else [tooling, "--mcp"]
    proc = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    failures = []
    ids = [0]

    def send(method, params=None):
        ids[0] += 1
        proc.stdin.write((json.dumps({"jsonrpc": "2.0", "id": ids[0], "method": method, "params": params or {}}) + "\n").encode())
        proc.stdin.flush()
        while True:
            reply = json.loads(proc.stdout.readline())
            if reply.get("id") == ids[0]:
                return reply

    def call(tool, arguments=None):
        result = send("tools/call", {"name": tool, "arguments": arguments or {}})["result"]
        text = "\n".join(c["text"] for c in result["content"] if c["type"] == "text")
        return text, result["isError"]

    def check(ok, what):
        print(("PASS " if ok else "FAIL ") + what, flush=True)
        if not ok:
            failures.append(what)

    try:
        init = send("initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "smoke", "version": "1"}})
        check(init["result"]["serverInfo"]["name"] == "winasm65", "initialize: " + init["result"]["protocolVersion"])
        proc.stdin.write(b'{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
        tools = [t["name"] for t in send("tools/list")["result"]["tools"]]
        check("build_project" in tools and "debug_launch" in tools, "tools/list: %d tools" % len(tools))

        text, error = call("build_project", {"config": config})
        check(not error and "Build succeeded." in text, "build_project: " + text.splitlines()[1] if not error else text)

        text, error = call("get_listing", {"config": config, "file": source, "fromLine": line, "toLine": line})
        check(not error and ("$" in text), "get_listing of line %d: %s" % (line, text.splitlines()[-1].strip()))

        text, error = call("describe_instruction", {"mnemonic": "LDA"})
        check(not error and "Load A" in text, "describe_instruction LDA")

        launch = {"config": config, "breakpoints": ["%s:%d" % (source, line)], "waitSeconds": 20}
        if emulator:
            launch["emulatorPath"] = emulator
        text, error = call("debug_launch", launch)
        check(not error and "Stopped in" in text, "debug_launch stops at the breakpoint: "
              + next((l for l in text.splitlines() if l.startswith("Stopped")), text[:200]))
        print("   " + "\n   ".join(text.splitlines()[:8]))

        text, error = call("get_state")
        check(not error and "Registers:" in text, "get_state")

        text, error = call("read_memory", {"space": "PAL", "address": "$00", "length": 32})
        check(not error and "$0000" in text, "read_memory PAL: " + (text.splitlines()[1] if not error else text))

        text, error = call("describe_machine")
        check(not error, "describe_machine: " + text.splitlines()[0])

        text, error = call("debug_stop")
        check(not error, "debug_stop")
    finally:
        proc.stdin.close()
        proc.wait(timeout=30)

    print("%d failure(s)" % len(failures))
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
