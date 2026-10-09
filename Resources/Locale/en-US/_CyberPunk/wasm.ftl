cmd-wasmtest-desc = Runs a built-in test program on a throwaway machine on the server's WASM host.
cmd-wasmtest-help = Usage: wasmtest <sample>
cmd-wasmtest-hint = <sample>
cmd-wasmtest-usage = Usage: wasmtest <sample>, where sample is one of: {$samples}
cmd-wasmtest-failed = The program couldn't be compiled: {$error}
cmd-wasmtest-result = {$state} after {$ticks} ticks, {$fuel} fuel used, {$ms} ms.

cmd-machine-desc = Works a WASM machine: shows its screen or programs, types at its terminal, or writes a file to its disk.
cmd-machine-help = Usage: machine <uid> screen | ps | type <text...> | write <file> <text...>
cmd-machine-hint-machine = <uid>
cmd-machine-hint-action = screen | ps | type | write
cmd-machine-not-a-machine = {$uid} isn't a WASM machine.
cmd-machine-ps = {$state}, up {$clock} ms. Terminal: {$programs}. Jobs: {$jobs}
cmd-machine-write-failed = Couldn't write the file: {$error}
