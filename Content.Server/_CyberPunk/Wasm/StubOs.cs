using System.Text;
using System.Text.RegularExpressions;

namespace Content.Server._CyberPunk.Wasm;

/// <summary>
/// The operating system computers boot until the real one is rewritten in Wire: a small shell over the disk,
/// hand-written in WAT against the same kernel functions any program uses.
/// </summary>
/// <remarks>
/// <para>
/// It reads typed lines, echoes them, and runs <c>help</c>, <c>man</c>, <c>ls</c>, <c>cat</c>, <c>write</c>,
/// <c>append</c>, <c>rm</c>, <c>new</c> (a Wire program to start from), <c>build</c> (from Wire or WAT), <c>run</c> (in front, or in the background with
/// <c>&amp;</c>), <c>jobs</c>, <c>kill</c>, <c>hostname</c>, <c>hosts</c>, <c>ip</c>, <c>echo</c>, <c>uptime</c>
/// and <c>ver</c>, with the same
/// messages as Switchboard's default OS. A program it runs takes over the terminal until it ends, and the
/// shell prompts again.
/// </para>
/// <para>
/// Output that doesn't fit in one tick's terminal budget (a long file or manual page) waits in a buffer and
/// goes out over the next ticks, a whole character at a time; typed lines wait until it's all out.
/// </para>
/// <para>
/// The program's text lives in <see cref="Template"/>, where <c>@name</c> stands for a string in
/// <see cref="Strings"/> as its address and length (<c>@name_at</c> and <c>@name_len</c> for each alone).
/// <see cref="Wat"/> lays the strings out in a data segment
/// and fills them in.
/// </para>
/// </remarks>
public static class StubOs
{
    /// <summary>
    /// The shell's name and version, as the banner and <c>ver</c> show it.
    /// </summary>
    public const string Name = "CyberPunk14 stub OS 0.1";

    private const string Help = """
        help                       this list
        man [TOPIC]                the manual
        ls                         list files
        cat FILE                   show a file
        write FILE TEXT...         write text to a file (replacing it)
        append FILE TEXT...        add a line of text to a file
        rm FILE                    delete a file
        new NAME [KIND]            start a Wire program, NAME.wire
        build FILE [OUT.wasm]      build a program from Wire (or WAT)
        run FILE [ARGS...] [&]     run a program (& in the background)
        jobs, kill N               background jobs: list, stop one
        hostname [NAME]            show or set this computer's network name
        hosts, ip                  names the router knows; this address
        echo TEXT...               print text
        uptime, ver                time since boot, versions

        """;

    private static readonly Regex StringRef = new("@([a-z_]+?)(_at|_len)?\\b");

    /// <summary>
    /// The strings the shell prints and the command names it knows.
    /// </summary>
    private static readonly (string Key, string Text)[] Strings =
    [
        ("prompt", "$ "),
        ("nl", "\n"),
        ("banner", $"{Name} (kernel v"),
        ("banner_end", ")\nType `help` for commands, `man` for the manual.\n"),
        ("ver", $"{Name}, kernel v"),
        ("help_text", Help),
        ("unknown", ": unknown command (try `help`)\n"),
        ("no_files", "(no files)\n"),
        ("no_such_file", ": no such file\n"),
        ("cat_", "cat: "),
        ("rm_", "rm: "),
        ("run_", "run: "),
        ("build_", "build: "),
        ("wrote", "wrote "),
        ("bytes_to", " bytes to "),
        ("bad_name", ": bad file name\n"),
        ("disk_full", ": disk full\n"),
        ("not_program", ": not a program (source code? `build` it first)\n"),
        ("too_deep", "run: too many programs running\n"),
        ("too_many_jobs", "run: too many jobs\n"),
        ("args_long", "run: arguments too long\n"),
        ("no_jobs", "(no background jobs)\n"),
        ("stopping", "stopping job "),
        ("no_job", "kill: no job "),
        ("see_jobs", " (see `jobs`)\n"),
        ("built", "built "),
        ("open_paren", " ("),
        ("bytes_close", " bytes)\n"),
        ("too_big", ": too big (64 KiB at most)\n"),
        ("colon_nl", ":\n"),
        ("cant_write", "build: can't write "),
        ("no_page", "man: no page on "),
        ("try_man", " (try `man`)\n"),
        ("up", "up "),
        ("seconds", " s\n"),
        ("open_bracket", "["),
        ("close_bracket", "] "),
        ("usage_cat", "usage: cat FILE\n"),
        ("usage_write", "usage: write FILE TEXT...\n"),
        ("usage_append", "usage: append FILE TEXT...\n"),
        ("usage_rm", "usage: rm FILE\n"),
        ("usage_run", "usage: run FILE [ARGS...] [&]\n"),
        ("usage_kill", "usage: kill N   (a job's number, from `jobs`)\n"),
        ("usage_build", "usage: build FILE.wire [OUT.wasm]   (or FILE.wat)\n"),
        ("usage_new", "usage: new NAME [computer|door|camera|ice|deck|implant]\n"),
        ("new_", "new: "),
        ("exists", " already exists\n"),
        ("no_kind", "new: no program for "),
        ("kinds", " (computer, door, camera, ice, deck or implant)\n"),
        ("comma_a", ", a "),
        ("next", " program. Next:\n  cat "),
        ("read_it", "   read it\n  build "),
        ("build_it", "   build it into "),
        ("run_it", "\n  run "),
        ("flash_it", "\n  flash ADDR "),
        ("hold_it", "\n  hold "),
        ("man_flash", "   (man flash)\n"),
        ("man_deck", "   (man deck)\n"),
        ("ext_wat", ".wat"),
        ("ext_wire", ".wire"),
        ("ext_wasm", ".wasm"),
        ("k_help", "help"),
        ("k_man", "man"),
        ("k_ls", "ls"),
        ("k_cat", "cat"),
        ("k_write", "write"),
        ("k_append", "append"),
        ("k_rm", "rm"),
        ("k_build", "build"),
        ("k_new", "new"),
        ("k_computer", "computer"),
        ("k_door", "door"),
        ("k_camera", "camera"),
        ("k_deck", "deck"),
        ("k_run", "run"),
        ("k_jobs", "jobs"),
        ("k_kill", "kill"),
        ("k_echo", "echo"),
        ("k_uptime", "uptime"),
        ("k_ver", "ver"),
        ("k_hostname", "hostname"),
        ("k_hosts", "hosts"),
        ("k_ip", "ip"),
        ("dot", "."),
        ("no_addr", "ip: no address (the network needs a powered router)\n"),
        ("bad_host", "hostname: a name is 1 to 32 of a-z, 0-9 and -\n"),
        ("no_hostname", "(no hostname: set one with `hostname NAME`)\n"),
        ("no_hosts", "(no hostnames known on this network)\n"),
    ];

    // Memory layout. Strings sit below Line; Out and Data are big enough for a whole file.
    // Line 4096 (512)   typed line being run
    // In   4608 (4096)  typed input not yet split into lines
    // Num  8704 (32)    a number being printed
    // Name 8800 (128)   the program file build writes to, or the file new writes
    // Out name 8928 (128)  the program new's file builds into
    // Scratch 16384 (65536)  file lists, job lists, build errors
    // Out  131072 (1114112)  output waiting to go to the terminal
    // Data 1245184 (1114112) a file being written
    private const string Template = """
        (module
          (import "sb_v6" "api_version" (func $api_version (result i32)))
          (import "sb_v6" "clock_ms" (func $clock_ms (result i64)))
          (import "sb_v6" "term_write" (func $term_write (param i32 i32)))
          (import "sb_v6" "term_read" (func $term_read (param i32 i32) (result i32)))
          (import "sb_v6" "fs_list" (func $fs_list (param i32 i32) (result i32)))
          (import "sb_v6" "fs_read" (func $fs_read (param i32 i32 i32 i32) (result i32)))
          (import "sb_v6" "fs_write" (func $fs_write (param i32 i32 i32 i32) (result i32)))
          (import "sb_v6" "fs_delete" (func $fs_delete (param i32 i32) (result i32)))
          (import "sb_v6" "exec_args" (func $exec_args (param i32 i32 i32 i32) (result i32)))
          (import "sb_v6" "job_start" (func $job_start (param i32 i32 i32 i32) (result i32)))
          (import "sb_v6" "job_list" (func $job_list (param i32 i32) (result i32)))
          (import "sb_v6" "job_kill" (func $job_kill (param i32) (result i32)))
          (import "sb_v6" "build" (func $build (param i32 i32 i32 i32 i32 i32) (result i32)))
          (import "sb_v6" "man" (func $man (param i32 i32 i32 i32) (result i32)))
          (import "sb_v6" "scaffold" (func $scaffold (param i32 i32 i32 i32) (result i32)))
          (import "sb_v6" "net_addr" (func $net_addr (result i64)))
          (import "sb_v6" "net_hostname" (func $net_hostname (param i32 i32) (result i32)))
          (import "sb_v6" "net_set_hostname" (func $net_set_hostname (param i32 i32) (result i32)))
          (import "sb_v6" "net_hosts" (func $net_hosts (param i32 i32) (result i32)))

          (memory (export "memory") 36)
          @data

          ;; Output waiting to go out: Out + [pend_start, pend_end).
          (global $pend_start (mut i32) (i32.const 0))
          (global $pend_end (mut i32) (i32.const 0))
          ;; Bytes the shell may still write this tick (the kernel allows 4096 a tick).
          (global $budget (mut i32) (i32.const 0))
          ;; Bytes of typed input waiting in In.
          (global $in_len (mut i32) (i32.const 0))
          (global $line_len (mut i32) (i32.const 0))
          ;; Whether a program we started has the terminal.
          (global $child (mut i32) (i32.const 0))
          ;; The command line's first word and the rest, and $split's last result.
          (global $cmd (mut i32) (i32.const 0))
          (global $cmd_len (mut i32) (i32.const 0))
          (global $rest (mut i32) (i32.const 0))
          (global $rest_len (mut i32) (i32.const 0))
          (global $w (mut i32) (i32.const 0))
          (global $w_len (mut i32) (i32.const 0))
          (global $r (mut i32) (i32.const 0))
          (global $r_len (mut i32) (i32.const 0))

          (func $room (result i32)
            (i32.sub (i32.const 1114112) (global.get $pend_end)))

          ;; Queues output, as much as fits.
          (func $out (param $p i32) (param $n i32)
            (if (i32.gt_u (local.get $n) (call $room))
              (then (local.set $n (call $room))))
            (memory.copy
              (i32.add (i32.const 131072) (global.get $pend_end))
              (local.get $p) (local.get $n))
            (global.set $pend_end (i32.add (global.get $pend_end) (local.get $n))))

          (func $nl (call $out @nl))

          (func $outnum (param $v i64)
            (local $p i32) (local $neg i32)
            (local.set $p (i32.const 8728))
            (if (i64.lt_s (local.get $v) (i64.const 0))
              (then
                (local.set $neg (i32.const 1))
                (local.set $v (i64.sub (i64.const 0) (local.get $v)))))
            (loop $digits
              (local.set $p (i32.sub (local.get $p) (i32.const 1)))
              (i32.store8 (local.get $p)
                (i32.add (i32.const 48) (i32.wrap_i64 (i64.rem_u (local.get $v) (i64.const 10)))))
              (local.set $v (i64.div_u (local.get $v) (i64.const 10)))
              (br_if $digits (i64.ne (local.get $v) (i64.const 0))))
            (if (local.get $neg)
              (then
                (local.set $p (i32.sub (local.get $p) (i32.const 1)))
                (i32.store8 (local.get $p) (i32.const 45))))
            (call $out (local.get $p) (i32.sub (i32.const 8728) (local.get $p))))

          ;; Sends as much waiting output as this tick allows, never half a character.
          (func $flush
            (local $n i32)
            (local.set $n (i32.sub (global.get $pend_end) (global.get $pend_start)))
            (if (i32.gt_u (local.get $n) (global.get $budget))
              (then
                (local.set $n (global.get $budget))
                (block $done
                  (loop $back
                    (br_if $done (i32.eqz (local.get $n)))
                    (br_if $done
                      (i32.ne
                        (i32.and
                          (i32.load8_u
                            (i32.add (i32.const 131072) (i32.add (global.get $pend_start) (local.get $n))))
                          (i32.const 192))
                        (i32.const 128)))
                    (local.set $n (i32.sub (local.get $n) (i32.const 1)))
                    (br $back)))))
            (if (local.get $n)
              (then
                (call $term_write (i32.add (i32.const 131072) (global.get $pend_start)) (local.get $n))
                (global.set $budget (i32.sub (global.get $budget) (local.get $n)))
                (global.set $pend_start (i32.add (global.get $pend_start) (local.get $n)))))
            (if (i32.eq (global.get $pend_start) (global.get $pend_end))
              (then
                (global.set $pend_start (i32.const 0))
                (global.set $pend_end (i32.const 0)))))

          (func $eq (param $a i32) (param $an i32) (param $b i32) (param $bn i32) (result i32)
            (local $i i32)
            (if (i32.ne (local.get $an) (local.get $bn))
              (then (return (i32.const 0))))
            (loop $each
              (if (i32.ge_u (local.get $i) (local.get $an))
                (then (return (i32.const 1))))
              (if (i32.ne
                    (i32.load8_u (i32.add (local.get $a) (local.get $i)))
                    (i32.load8_u (i32.add (local.get $b) (local.get $i))))
                (then (return (i32.const 0))))
              (local.set $i (i32.add (local.get $i) (i32.const 1)))
              (br $each))
            (unreachable))

          (func $is (param $s i32) (param $n i32) (result i32)
            (call $eq (global.get $cmd) (global.get $cmd_len) (local.get $s) (local.get $n)))

          ;; Whether [p, p+n) ends with the string [s, s+sn).
          (func $ends_with (param $p i32) (param $n i32) (param $s i32) (param $sn i32) (result i32)
            (if (i32.lt_u (local.get $n) (local.get $sn))
              (then (return (i32.const 0))))
            (call $eq
              (i32.add (local.get $p) (i32.sub (local.get $n) (local.get $sn))) (local.get $sn)
              (local.get $s) (local.get $sn)))

          ;; Splits [p, p+n) into its first word ($w) and the rest ($r), both trimmed of spaces.
          (func $split (param $p i32) (param $n i32)
            (local $end i32) (local $q i32)
            (local.set $end (i32.add (local.get $p) (local.get $n)))
            (block $done
              (loop $lead
                (br_if $done (i32.ge_u (local.get $p) (local.get $end)))
                (br_if $done (i32.ne (i32.load8_u (local.get $p)) (i32.const 32)))
                (local.set $p (i32.add (local.get $p) (i32.const 1)))
                (br $lead)))
            (block $done
              (loop $trail
                (br_if $done (i32.le_u (local.get $end) (local.get $p)))
                (br_if $done
                  (i32.ne (i32.load8_u (i32.sub (local.get $end) (i32.const 1))) (i32.const 32)))
                (local.set $end (i32.sub (local.get $end) (i32.const 1)))
                (br $trail)))
            (local.set $q (local.get $p))
            (block $done
              (loop $word
                (br_if $done (i32.ge_u (local.get $q) (local.get $end)))
                (br_if $done (i32.eq (i32.load8_u (local.get $q)) (i32.const 32)))
                (local.set $q (i32.add (local.get $q) (i32.const 1)))
                (br $word)))
            (global.set $w (local.get $p))
            (global.set $w_len (i32.sub (local.get $q) (local.get $p)))
            (block $done
              (loop $gap
                (br_if $done (i32.ge_u (local.get $q) (local.get $end)))
                (br_if $done (i32.ne (i32.load8_u (local.get $q)) (i32.const 32)))
                (local.set $q (i32.add (local.get $q) (i32.const 1)))
                (br $gap)))
            (global.set $r (local.get $q))
            (global.set $r_len (i32.sub (local.get $end) (local.get $q))))

          ;; The length of the zero-terminated text at p, up to n.
          (func $strlen (param $p i32) (param $n i32) (result i32)
            (local $i i32)
            (block $done
              (loop $each
                (br_if $done (i32.ge_u (local.get $i) (local.get $n)))
                (br_if $done (i32.eqz (i32.load8_u (i32.add (local.get $p) (local.get $i)))))
                (local.set $i (i32.add (local.get $i) (i32.const 1)))
                (br $each)))
            (local.get $i))

          (func $ls
            (local $n i32)
            (local.set $n (call $fs_list (i32.const 16384) (i32.const 65536)))
            (if (i32.eqz (local.get $n))
              (then (call $out @no_files) (return)))
            (if (i32.gt_u (local.get $n) (i32.const 65536))
              (then (local.set $n (i32.const 65536))))
            (call $out (i32.const 16384) (local.get $n))
            (call $nl))

          (func $cat
            (local $n i32) (local $room i32)
            (if (i32.eqz (global.get $rest_len))
              (then (call $out @usage_cat) (return)))
            (local.set $room (call $room))
            (local.set $n
              (call $fs_read (global.get $rest) (global.get $rest_len)
                (i32.add (i32.const 131072) (global.get $pend_end)) (local.get $room)))
            (if (i32.lt_s (local.get $n) (i32.const 0))
              (then
                (call $out @cat_)
                (call $out (global.get $rest) (global.get $rest_len))
                (call $out @no_such_file)
                (return)))
            (if (i32.gt_u (local.get $n) (local.get $room))
              (then (local.set $n (local.get $room))))
            (global.set $pend_end (i32.add (global.get $pend_end) (local.get $n)))
            (if (i32.and
                  (i32.ne (local.get $n) (i32.const 0))
                  (i32.ne
                    (i32.load8_u (i32.add (i32.const 131072) (i32.sub (global.get $pend_end) (i32.const 1))))
                    (i32.const 10)))
              (then (call $nl))))

          ;; write and append: FILE TEXT...
          (func $write (param $append i32)
            (local $len i32) (local $n i32) (local $res i32)
            (call $split (global.get $rest) (global.get $rest_len))
            (if (i32.eqz (global.get $w_len))
              (then
                (if (local.get $append)
                  (then (call $out @usage_append))
                  (else (call $out @usage_write)))
                (return)))
            (if (local.get $append)
              (then
                (local.set $n (call $fs_read (global.get $w) (global.get $w_len) (i32.const 1245184) (i32.const 1114112)))
                (if (i32.gt_s (local.get $n) (i32.const 0))
                  (then (local.set $len (local.get $n))))))
            (memory.copy (i32.add (i32.const 1245184) (local.get $len)) (global.get $r) (global.get $r_len))
            (local.set $len (i32.add (local.get $len) (global.get $r_len)))
            (i32.store8 (i32.add (i32.const 1245184) (local.get $len)) (i32.const 10))
            (local.set $len (i32.add (local.get $len) (i32.const 1)))
            (local.set $res (call $fs_write (global.get $w) (global.get $w_len) (i32.const 1245184) (local.get $len)))
            (if (i32.eqz (local.get $res))
              (then
                (call $out @wrote)
                (call $outnum (i64.extend_i32_u (local.get $len)))
                (call $out @bytes_to)
                (call $out (global.get $w) (global.get $w_len))
                (call $nl)
                (return)))
            (call $out (global.get $cmd) (global.get $cmd_len))
            (if (i32.eq (local.get $res) (i32.const -1))
              (then (call $out @bad_name))
              (else (call $out @disk_full))))

          (func $rm
            (if (i32.eqz (global.get $rest_len))
              (then (call $out @usage_rm) (return)))
            (if (i32.lt_s (call $fs_delete (global.get $rest) (global.get $rest_len)) (i32.const 0))
              (then
                (call $out @rm_)
                (call $out (global.get $rest) (global.get $rest_len))
                (call $out @no_such_file))))

          ;; Why a program couldn't be run or started as a job.
          (func $run_error (param $code i32) (param $job i32)
            (if (i32.eq (local.get $code) (i32.const -1))
              (then
                (call $out @run_)
                (call $out (global.get $w) (global.get $w_len))
                (call $out @no_such_file)
                (return)))
            (if (i32.eq (local.get $code) (i32.const -2))
              (then
                (call $out @run_)
                (call $out (global.get $w) (global.get $w_len))
                (call $out @not_program)
                (return)))
            (if (i32.eq (local.get $code) (i32.const -3))
              (then
                (if (local.get $job)
                  (then (call $out @too_many_jobs))
                  (else (call $out @too_deep)))
                (return)))
            (call $out @args_long))

          ;; run FILE [ARGS...] [&]. Returns 1 if a program took over the terminal.
          (func $run (result i32)
            (local $len i32) (local $job i32) (local $res i32)
            (local.set $len (global.get $rest_len))
            (if (i32.and
                  (i32.ne (local.get $len) (i32.const 0))
                  (i32.eq
                    (i32.load8_u (i32.add (global.get $rest) (i32.sub (local.get $len) (i32.const 1))))
                    (i32.const 38)))
              (then
                (local.set $job (i32.const 1))
                (local.set $len (i32.sub (local.get $len) (i32.const 1)))))
            (call $split (global.get $rest) (local.get $len))
            (if (i32.eqz (global.get $w_len))
              (then (call $out @usage_run) (return (i32.const 0))))
            (if (local.get $job)
              (then
                (local.set $res
                  (call $job_start (global.get $w) (global.get $w_len) (global.get $r) (global.get $r_len)))
                (if (i32.ge_s (local.get $res) (i32.const 0))
                  (then
                    (call $out @open_bracket)
                    (call $outnum (i64.extend_i32_u (local.get $res)))
                    (call $out @close_bracket)
                    (call $out (global.get $w) (global.get $w_len))
                    (call $nl))
                  (else (call $run_error (local.get $res) (i32.const 1))))
                (return (i32.const 0))))
            (local.set $res
              (call $exec_args (global.get $w) (global.get $w_len) (global.get $r) (global.get $r_len)))
            (if (i32.eqz (local.get $res))
              (then (return (i32.const 1))))
            (call $run_error (local.get $res) (i32.const 0))
            (i32.const 0))

          (func $jobs
            (local $n i32)
            (local.set $n (call $job_list (i32.const 16384) (i32.const 65536)))
            (if (i32.eqz (local.get $n))
              (then (call $out @no_jobs) (return)))
            (if (i32.gt_u (local.get $n) (i32.const 65536))
              (then (local.set $n (i32.const 65536))))
            (call $out (i32.const 16384) (local.get $n)))

          (func $kill
            (local $p i32) (local $end i32) (local $c i32) (local $v i32)
            (local.set $p (global.get $rest))
            (local.set $end (i32.add (global.get $rest) (global.get $rest_len)))
            (if (i32.and
                  (i32.lt_u (local.get $p) (local.get $end))
                  (i32.eq (i32.load8_u (local.get $p)) (i32.const 37)))
              (then (local.set $p (i32.add (local.get $p) (i32.const 1)))))
            (if (i32.ge_u (local.get $p) (local.get $end))
              (then (call $out @usage_kill) (return)))
            (block $done
              (loop $digits
                (br_if $done (i32.ge_u (local.get $p) (local.get $end)))
                (local.set $c (i32.load8_u (local.get $p)))
                (if (i32.or
                      (i32.or (i32.lt_u (local.get $c) (i32.const 48)) (i32.gt_u (local.get $c) (i32.const 57)))
                      (i32.gt_u (local.get $v) (i32.const 1000000)))
                  (then (call $out @usage_kill) (return)))
                (local.set $v
                  (i32.add (i32.mul (local.get $v) (i32.const 10)) (i32.sub (local.get $c) (i32.const 48))))
                (local.set $p (i32.add (local.get $p) (i32.const 1)))
                (br $digits)))
            (if (i32.eqz (call $job_kill (local.get $v)))
              (then
                (call $out @stopping)
                (call $outnum (i64.extend_i32_u (local.get $v)))
                (call $nl))
              (else
                (call $out @no_job)
                (call $outnum (i64.extend_i32_u (local.get $v)))
                (call $out @see_jobs))))

          ;; build SRC [OUT]: OUT defaults to SRC with .wat (or .wire) swapped for .wasm.
          (func $build_cmd
            (local $out i32) (local $out_len i32) (local $base i32) (local $res i32)
            (call $split (global.get $rest) (global.get $rest_len))
            (if (i32.eqz (global.get $w_len))
              (then (call $out @usage_build) (return)))
            (local.set $out (global.get $r))
            (local.set $out_len (global.get $r_len))
            (if (i32.eqz (local.get $out_len))
              (then
                (local.set $base (global.get $w_len))
                (if (call $ends_with (global.get $w) (global.get $w_len) @ext_wat)
                  (then (local.set $base (i32.sub (local.get $base) (i32.const 4)))))
                (if (call $ends_with (global.get $w) (global.get $w_len) @ext_wire)
                  (then (local.set $base (i32.sub (local.get $base) (i32.const 5)))))
                (if (i32.gt_u (local.get $base) (i32.const 100))
                  (then (local.set $base (i32.const 100))))
                (memory.copy (i32.const 8800) (global.get $w) (local.get $base))
                (memory.copy (i32.add (i32.const 8800) (local.get $base)) @ext_wasm)
                (local.set $out (i32.const 8800))
                (local.set $out_len (i32.add (local.get $base) (i32.const 5)))))
            (memory.fill (i32.const 16384) (i32.const 0) (i32.const 4096))
            (local.set $res
              (call $build (global.get $w) (global.get $w_len) (local.get $out) (local.get $out_len)
                (i32.const 16384) (i32.const 4095)))
            (if (i32.ge_s (local.get $res) (i32.const 0))
              (then
                (call $out @built)
                (call $out (local.get $out) (local.get $out_len))
                (call $out @open_paren)
                (call $outnum (i64.extend_i32_u (local.get $res)))
                (call $out @bytes_close)
                (return)))
            (if (i32.eq (local.get $res) (i32.const -4))
              (then
                (call $out @cant_write)
                (call $out (local.get $out) (local.get $out_len))
                (call $nl)
                (return)))
            (call $out @build_)
            (call $out (global.get $w) (global.get $w_len))
            (if (i32.eq (local.get $res) (i32.const -1))
              (then (call $out @no_such_file) (return)))
            (if (i32.eq (local.get $res) (i32.const -2))
              (then (call $out @too_big) (return)))
            (call $out @colon_nl)
            (call $out (i32.const 16384) (call $strlen (i32.const 16384) (i32.const 4095)))
            (call $nl))

          ;; new NAME [KIND]: writes NAME.wire, a Wire program to start from, for a kind of machine.
          (func $new_cmd
            (local $kind i32) (local $kind_len i32) (local $len i32) (local $base i32) (local $n i32) (local $res i32)
            (call $split (global.get $rest) (global.get $rest_len))
            (if (i32.eqz (global.get $w_len))
              (then (call $out @usage_new) (return)))
            (local.set $kind (global.get $r))
            (local.set $kind_len (global.get $r_len))
            (if (i32.eqz (local.get $kind_len))
              (then
                (local.set $kind (i32.const @k_computer_at))
                (local.set $kind_len (i32.const @k_computer_len))))
            ;; The file is NAME.wire, and it builds into NAME.wasm.
            (local.set $base (global.get $w_len))
            (if (call $ends_with (global.get $w) (global.get $w_len) @ext_wire)
              (then (local.set $base (i32.sub (local.get $base) (i32.const 5)))))
            (if (i32.gt_u (local.get $base) (i32.const 100))
              (then (local.set $base (i32.const 100))))
            (memory.copy (i32.const 8800) (global.get $w) (local.get $base))
            (memory.copy (i32.add (i32.const 8800) (local.get $base)) @ext_wire)
            (local.set $len (i32.add (local.get $base) (i32.const 5)))
            (memory.copy (i32.const 8928) (global.get $w) (local.get $base))
            (memory.copy (i32.add (i32.const 8928) (local.get $base)) @ext_wasm)
            (if (i32.ge_s (call $fs_read (i32.const 8800) (local.get $len) (i32.const 16384) (i32.const 0)) (i32.const 0))
              (then
                (call $out @new_)
                (call $out (i32.const 8800) (local.get $len))
                (call $out @exists)
                (return)))
            (local.set $n
              (call $scaffold (local.get $kind) (local.get $kind_len) (i32.const 1245184) (i32.const 1114112)))
            (if (i32.lt_s (local.get $n) (i32.const 0))
              (then
                (call $out @no_kind)
                (call $out (local.get $kind) (local.get $kind_len))
                (call $out @kinds)
                (return)))
            (local.set $res (call $fs_write (i32.const 8800) (local.get $len) (i32.const 1245184) (local.get $n)))
            (if (i32.ne (local.get $res) (i32.const 0))
              (then
                (call $out @new_)
                (call $out (i32.const 8800) (local.get $len))
                (if (i32.eq (local.get $res) (i32.const -1))
                  (then (call $out @bad_name))
                  (else (call $out @disk_full)))
                (return)))
            (call $out @wrote)
            (call $out (i32.const 8800) (local.get $len))
            (call $out @comma_a)
            (call $out (local.get $kind) (local.get $kind_len))
            (call $out @next)
            (call $out (i32.const 8800) (local.get $len))
            (call $out @read_it)
            (call $out (i32.const 8800) (local.get $len))
            (call $out @build_it)
            (call $out (i32.const 8928) (i32.add (local.get $base) (i32.const 5)))
            ;; Doors and cameras take it by flashing; a deck holds it; the rest run it.
            (if (i32.or (call $eq (local.get $kind) (local.get $kind_len) @k_door)
                  (call $eq (local.get $kind) (local.get $kind_len) @k_camera))
              (then
                (call $out @flash_it)
                (call $out (i32.const 8928) (i32.add (local.get $base) (i32.const 5)))
                (call $out @man_flash)
                (return)))
            (if (call $eq (local.get $kind) (local.get $kind_len) @k_deck)
              (then
                (call $out @hold_it)
                (call $out (i32.const 8928) (i32.add (local.get $base) (i32.const 5)))
                (call $out @man_deck)
                (return)))
            (call $out @run_it)
            (call $out (i32.const 8928) (i32.add (local.get $base) (i32.const 5)))
            (call $nl))

          (func $man_cmd
            (local $n i32) (local $room i32)
            (local.set $room (call $room))
            (local.set $n
              (call $man (global.get $rest) (global.get $rest_len)
                (i32.add (i32.const 131072) (global.get $pend_end)) (local.get $room)))
            (if (i32.lt_s (local.get $n) (i32.const 0))
              (then
                (call $out @no_page)
                (call $out (global.get $rest) (global.get $rest_len))
                (call $out @try_man)
                (return)))
            (if (i32.gt_u (local.get $n) (local.get $room))
              (then (local.set $n (local.get $room))))
            (global.set $pend_end (i32.add (global.get $pend_end) (local.get $n))))

          ;; An address as it's written, like 10.2.1.1.
          (func $outaddr (param $a i32)
            (call $outnum (i64.extend_i32_u (i32.shr_u (local.get $a) (i32.const 24))))
            (call $out @dot)
            (call $outnum (i64.extend_i32_u (i32.and (i32.shr_u (local.get $a) (i32.const 16)) (i32.const 255))))
            (call $out @dot)
            (call $outnum (i64.extend_i32_u (i32.and (i32.shr_u (local.get $a) (i32.const 8)) (i32.const 255))))
            (call $out @dot)
            (call $outnum (i64.extend_i32_u (i32.and (local.get $a) (i32.const 255)))))

          (func $ip
            (local $a i64)
            (local.set $a (call $net_addr))
            (if (i64.lt_s (local.get $a) (i64.const 0))
              (then (call $out @no_addr) (return)))
            (call $outaddr (i32.wrap_i64 (local.get $a)))
            (call $nl))

          ;; hostname shows the name; hostname NAME sets it.
          (func $hostname
            (local $n i32)
            (if (global.get $rest_len)
              (then
                (if (call $net_set_hostname (global.get $rest) (global.get $rest_len))
                  (then (call $out @bad_host)))
                (return)))
            (local.set $n (call $net_hostname (i32.const 16384) (i32.const 64)))
            (if (i32.eqz (local.get $n))
              (then (call $out @no_hostname) (return)))
            (call $out (i32.const 16384) (local.get $n))
            (call $nl))

          (func $hosts
            (local $n i32)
            (local.set $n (call $net_hosts (i32.const 16384) (i32.const 65536)))
            (if (i32.eqz (local.get $n))
              (then (call $out @no_hosts) (return)))
            (if (i32.gt_u (local.get $n) (i32.const 65536))
              (then (local.set $n (i32.const 65536))))
            (call $out (i32.const 16384) (local.get $n)))

          ;; Runs the typed line in Line. Returns 1 if a program took over the terminal.
          (func $run_command (result i32)
            (call $split (i32.const 4096) (global.get $line_len))
            (global.set $cmd (global.get $w))
            (global.set $cmd_len (global.get $w_len))
            (global.set $rest (global.get $r))
            (global.set $rest_len (global.get $r_len))
            (if (i32.eqz (global.get $cmd_len)) (then (return (i32.const 0))))
            (if (call $is @k_help) (then (call $out @help_text) (return (i32.const 0))))
            (if (call $is @k_man) (then (call $man_cmd) (return (i32.const 0))))
            (if (call $is @k_ls) (then (call $ls) (return (i32.const 0))))
            (if (call $is @k_cat) (then (call $cat) (return (i32.const 0))))
            (if (call $is @k_write) (then (call $write (i32.const 0)) (return (i32.const 0))))
            (if (call $is @k_append) (then (call $write (i32.const 1)) (return (i32.const 0))))
            (if (call $is @k_rm) (then (call $rm) (return (i32.const 0))))
            (if (call $is @k_build) (then (call $build_cmd) (return (i32.const 0))))
            (if (call $is @k_new) (then (call $new_cmd) (return (i32.const 0))))
            (if (call $is @k_run) (then (return (call $run))))
            (if (call $is @k_jobs) (then (call $jobs) (return (i32.const 0))))
            (if (call $is @k_kill) (then (call $kill) (return (i32.const 0))))
            (if (call $is @k_hostname) (then (call $hostname) (return (i32.const 0))))
            (if (call $is @k_hosts) (then (call $hosts) (return (i32.const 0))))
            (if (call $is @k_ip) (then (call $ip) (return (i32.const 0))))
            (if (call $is @k_echo)
              (then
                (call $out (global.get $rest) (global.get $rest_len))
                (call $nl)
                (return (i32.const 0))))
            (if (call $is @k_uptime)
              (then
                (call $out @up)
                (call $outnum (i64.div_u (call $clock_ms) (i64.const 1000)))
                (call $out @seconds)
                (return (i32.const 0))))
            (if (call $is @k_ver)
              (then
                (call $out @ver)
                (call $outnum (i64.extend_i32_s (call $api_version)))
                (call $nl)
                (return (i32.const 0))))
            (call $out (global.get $cmd) (global.get $cmd_len))
            (call $out @unknown)
            (i32.const 0))

          (func (export "start")
            (global.set $budget (i32.const 3800))
            (call $out @banner)
            (call $outnum (i64.extend_i32_s (call $api_version)))
            (call $out @banner_end)
            (call $out @prompt)
            (call $flush))

          (func (export "tick")
            (local $i i32)
            (global.set $budget (i32.const 3800))
            ;; Our tick only runs again once a program we started has ended.
            (if (global.get $child)
              (then
                (global.set $child (i32.const 0))
                (call $out @prompt)))
            (call $flush)
            (if (global.get $pend_end) (then (return)))
            (loop $lines
              (global.set $in_len
                (i32.add (global.get $in_len)
                  (call $term_read
                    (i32.add (i32.const 4608) (global.get $in_len))
                    (i32.sub (i32.const 4096) (global.get $in_len)))))
              ;; Find the end of the first line; wait for more if it isn't there.
              (local.set $i (i32.const 0))
              (block $found
                (loop $scan
                  (if (i32.ge_u (local.get $i) (global.get $in_len))
                    (then
                      ;; A line too long to ever end is dropped.
                      (if (i32.eq (global.get $in_len) (i32.const 4096))
                        (then (global.set $in_len (i32.const 0))))
                      (return)))
                  (br_if $found (i32.eq (i32.load8_u (i32.add (i32.const 4608) (local.get $i))) (i32.const 10)))
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br $scan)))
              (global.set $line_len (local.get $i))
              (if (i32.gt_u (global.get $line_len) (i32.const 512))
                (then (global.set $line_len (i32.const 512))))
              (memory.copy (i32.const 4096) (i32.const 4608) (global.get $line_len))
              (memory.copy
                (i32.const 4608)
                (i32.add (i32.const 4609) (local.get $i))
                (i32.sub (global.get $in_len) (i32.add (local.get $i) (i32.const 1))))
              (global.set $in_len (i32.sub (global.get $in_len) (i32.add (local.get $i) (i32.const 1))))
              ;; Echo it, as a terminal does, and run it.
              (call $out (i32.const 4096) (global.get $line_len))
              (call $nl)
              (if (call $run_command)
                (then
                  ;; A program took over the terminal; wait for it.
                  (global.set $child (i32.const 1))
                  (call $flush)
                  (return)))
              (call $out @prompt)
              (call $flush)
              (if (global.get $pend_end) (then (return)))
              (br $lines))))
        """;

    private const int StringsEnd = 4096;

    private static string? _wat;

    /// <summary>
    /// The shell's WAT, with its strings laid out.
    /// </summary>
    public static string Wat => _wat ??= Build();

    private static string Build()
    {
        var offsets = new Dictionary<string, (int Offset, int Length)>();
        var data = new StringBuilder();
        var offset = 16;
        foreach (var (key, text) in Strings)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            offsets[key] = (offset, bytes.Length);
            data.Append($"(data (i32.const {offset}) \"{Escape(bytes)}\")\n  ");
            offset += bytes.Length;
        }

        if (offset > StringsEnd)
            throw new InvalidOperationException($"The stub OS's strings take {offset} bytes, past {StringsEnd}.");

        var wat = Template.Replace("@data", data.ToString().TrimEnd());
        return StringRef.Replace(wat, m =>
        {
            if (!offsets.TryGetValue(m.Groups[1].Value, out var s))
                throw new InvalidOperationException($"The stub OS uses a string it doesn't have: {m.Value}");

            // @name is the string's address and length; @name_at and @name_len are each on its own.
            return m.Groups[2].Value switch
            {
                "_at" => s.Offset.ToString(),
                "_len" => s.Length.ToString(),
                _ => $"(i32.const {s.Offset}) (i32.const {s.Length})",
            };
        });
    }

    private static string Escape(byte[] bytes)
    {
        var escaped = new StringBuilder();
        foreach (var b in bytes)
        {
            if (b is >= 0x20 and < 0x7f && b != '"' && b != '\\')
                escaped.Append((char) b);
            else
                escaped.Append($"\\{b:x2}");
        }

        return escaped.ToString();
    }
}
