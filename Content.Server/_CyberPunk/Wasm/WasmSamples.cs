namespace Content.Server._CyberPunk.Wasm;

/// <summary>
/// Small hand-written programs that check the WASM host works, for the <c>wasmtest</c> command and tests.
/// Each runs as a machine's firmware.
/// </summary>
public static class WasmSamples
{
    /// <summary>
    /// Prints a line through the kernel's <c>term_write</c>.
    /// </summary>
    public const string Hello = """
        (module
          (import "sb_v4" "term_write" (func $term_write (param i32 i32)))
          (memory (export "memory") 1)
          (data (i32.const 0) "Hello from WASM!\n")
          (func (export "start")
            (call $term_write (i32.const 0) (i32.const 17))))
        """;

    /// <summary>
    /// Loops forever; only its fuel budget stops it.
    /// </summary>
    public const string Spin = """
        (module
          (memory (export "memory") 1)
          (func (export "start")
            (loop $forever
              (br $forever))))
        """;

    /// <summary>
    /// Builds a linked list of GC structs, sums it and prints whether the sum is right. Needs the GC
    /// proposal, which Wire's compiled programs may use for lists and dicts.
    /// </summary>
    public const string Gc = """
        (module
          (import "sb_v4" "term_write" (func $term_write (param i32 i32)))
          (type $node (struct (field $value i32) (field $next (ref null $node))))
          (memory (export "memory") 1)
          (data (i32.const 0) "GC ok\n")
          (data (i32.const 16) "GC wrong\n")
          (func (export "start")
            (local $list (ref null $node))
            (local $i i32)
            (local $sum i32)
            ;; Build the list 1..100.
            (local.set $i (i32.const 1))
            (loop $build
              (local.set $list (struct.new $node (local.get $i) (local.get $list)))
              (local.set $i (i32.add (local.get $i) (i32.const 1)))
              (br_if $build (i32.le_s (local.get $i) (i32.const 100))))
            ;; Sum it.
            (block $done
              (loop $walk
                (br_if $done (ref.is_null (local.get $list)))
                (local.set $sum (i32.add (local.get $sum) (struct.get $node $value (local.get $list))))
                (local.set $list (struct.get $node $next (local.get $list)))
                (br $walk)))
            (if (i32.eq (local.get $sum) (i32.const 5050))
              (then (call $term_write (i32.const 0) (i32.const 6)))
              (else (call $term_write (i32.const 16) (i32.const 9))))))
        """;

    /// <summary>
    /// Hands <c>term_write</c> a pointer past the end of its memory, which must kill it.
    /// </summary>
    public const string BadPointer = """
        (module
          (import "sb_v4" "term_write" (func $term_write (param i32 i32)))
          (memory (export "memory") 1)
          (func (export "start")
            (call $term_write (i32.const 65530) (i32.const 100))))
        """;

    /// <summary>
    /// Tries to grow its memory past the 8 MiB cap, then prints whether the host refused.
    /// </summary>
    public const string Grow = """
        (module
          (import "sb_v4" "term_write" (func $term_write (param i32 i32)))
          (memory (export "memory") 1)
          (data (i32.const 0) "refused\n")
          (data (i32.const 16) "allowed\n")
          (func (export "start")
            ;; 200 pages of 64 KiB is 12.5 MiB.
            (if (i32.eq (memory.grow (i32.const 200)) (i32.const -1))
              (then (call $term_write (i32.const 0) (i32.const 8)))
              (else (call $term_write (i32.const 16) (i32.const 8))))))
        """;
}
