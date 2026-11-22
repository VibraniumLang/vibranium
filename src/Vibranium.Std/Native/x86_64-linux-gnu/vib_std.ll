; =============================================================================
; vib_std.ll  --  Vibranium.Std trusted native leaves (T0) (v0.2, Zero C)
; Target: x86_64-pc-linux-gnu (glibc >= 2.33), LLVM 18. Appended after vib_rt.ll.
; Every leaf uses the ordinary Vibranium function ABI:
;   int -> i32, long -> i64, Span<T>/ReadOnlySpan<T> -> (ptr, i32 length)
; Return convention: >= 0 success; < 0 is -(int)IoErrorKind (normalised).
; =============================================================================

declare i32 @open(ptr nocapture readonly, i32, ...)
declare i64 @read(i32, ptr nocapture writeonly, i64)
declare i32 @close(i32)
declare i32 @fsync(i32)
declare i32 @fstat(i32, ptr)
declare i32 @stat(ptr nocapture readonly, ptr)
declare i32 @lstat(ptr nocapture readonly, ptr)
declare i64 @strlen(ptr nocapture readonly)
declare i32 @socket(i32, i32, i32)
declare i32 @setsockopt(i32, i32, i32, ptr, i32)
declare i32 @bind(i32, ptr, i32)
declare i32 @listen(i32, i32)
declare i32 @accept4(i32, ptr, ptr, i32)
declare i32 @connect(i32, ptr, i32)
declare i64 @recv(i32, ptr, i64, i32)
declare i64 @send(i32, ptr, i64, i32)
declare i32 @shutdown(i32, i32)
declare i32 @getsockname(i32, ptr, ptr)
declare i32 @posix_spawn(ptr, ptr, ptr, ptr, ptr, ptr)
declare i32 @waitpid(i32, ptr, i32)
declare i32 @llvm.bswap.i32(i32)
declare i16 @llvm.bswap.i16(i16)
@environ = external global ptr

define internal i32 @__vib_sys_kind_fs(i32 %e) nounwind {
entry:
  switch i32 %e, label %other [
    i32 2,  label %notfound      ; ENOENT
    i32 20, label %notfound      ; ENOTDIR
    i32 13, label %denied        ; EACCES
    i32 1,  label %denied        ; EPERM
    i32 21, label %isdir         ; EISDIR
    i32 36, label %invalid       ; ENAMETOOLONG
  ]
notfound:
  ret i32 2
denied:
  ret i32 3
isdir:
  ret i32 5
invalid:
  ret i32 6
other:
  ret i32 1
}

define internal i32 @__vib_sys_kind_net(i32 %e) nounwind {
entry:
  switch i32 %e, label %other [
    i32 13,  label %denied       ; EACCES
    i32 1,   label %denied       ; EPERM
    i32 4,   label %intr         ; EINTR
    i32 32,  label %pipe         ; EPIPE
    i32 111, label %refused      ; ECONNREFUSED
    i32 104, label %reset        ; ECONNRESET
    i32 103, label %aborted      ; ECONNABORTED
    i32 107, label %notconn      ; ENOTCONN
    i32 98,  label %inuse        ; EADDRINUSE
    i32 99,  label %notavail     ; EADDRNOTAVAIL
    i32 110, label %timedout     ; ETIMEDOUT
  ]
denied:
  ret i32 3
intr:
  ret i32 10
pipe:
  ret i32 11
refused:
  ret i32 12
reset:
  ret i32 13
aborted:
  ret i32 14
notconn:
  ret i32 15
inuse:
  ret i32 16
notavail:
  ret i32 17
timedout:
  ret i32 18
other:
  ret i32 1
}

define internal i32 @__vib_sys_errno() nounwind {
entry:
  %p = call ptr @__errno_location()
  %e = load i32, ptr %p
  ret i32 %e
}

; =============================== Fs ==========================================
; Sys.FileOpen(ReadOnlySpan<byte> path, int mode) -> int      mode: 0 = read, 1 = create/truncate
;   open(path, O_RDONLY|O_CLOEXEC|O_NOCTTY = 0x80100) or (O_WRONLY|O_CREAT|O_TRUNC|O_CLOEXEC|O_NOCTTY = 0x80341, 0666)
define internal i32 @__vib_sys_file_open(ptr readonly %path.ptr, i32 %path.len, i32 %mode) nounwind {
entry:
  %buf = alloca [4096 x i8], align 1
  %st = alloca [144 x i8], align 8
  %nonpos = icmp sle i32 %path.len, 0
  %toolong = icmp ugt i32 %path.len, 4095
  %badlen = or i1 %nonpos, %toolong
  br i1 %badlen, label %invalid, label %copy
copy:
  %len64 = zext i32 %path.len to i64
  call void @llvm.memcpy.p0.p0.i64(ptr %buf, ptr %path.ptr, i64 %len64, i1 false)
  %nul = getelementptr inbounds i8, ptr %buf, i64 %len64
  store i8 0, ptr %nul
  %iscreate = icmp eq i32 %mode, 1
  %isappend = icmp eq i32 %mode, 2
  %flags0 = select i1 %iscreate, i32 525121, i32 524544
  %flags = select i1 %isappend, i32 525633, i32 %flags0
  br label %try
try:
  %fd = call i32 (ptr, i32, ...) @open(ptr %buf, i32 %flags, i32 438)
  %failed = icmp slt i32 %fd, 0
  br i1 %failed, label %openerr, label %opened
openerr:
  %e = call i32 @__vib_sys_errno()
  %intr = icmp eq i32 %e, 4
  br i1 %intr, label %try, label %classify
classify:
  %k = call i32 @__vib_sys_kind_fs(i32 %e)
  %isdenied = icmp eq i32 %k, 3
  br i1 %isdenied, label %dircheck, label %fail
dircheck:
  %sr = call i32 @stat(ptr %buf, ptr %st)
  %sok = icmp eq i32 %sr, 0
  br i1 %sok, label %dirmode, label %fail
dirmode:
  %m0.p = getelementptr inbounds i8, ptr %st, i64 24
  %m0 = load i32, ptr %m0.p, align 4
  %t0 = and i32 %m0, 61440
  %d0 = icmp eq i32 %t0, 16384
  %k2 = select i1 %d0, i32 5, i32 3
  %neg2 = sub i32 0, %k2
  ret i32 %neg2
fail:
  %neg = sub i32 0, %k
  ret i32 %neg
opened:
  %fr = call i32 @fstat(i32 %fd, ptr %st)
  %fok = icmp eq i32 %fr, 0
  br i1 %fok, label %modecheck, label %closeother
modecheck:
  %m.p = getelementptr inbounds i8, ptr %st, i64 24
  %m = load i32, ptr %m.p, align 4
  %t = and i32 %m, 61440
  %isdir = icmp eq i32 %t, 16384
  br i1 %isdir, label %closedir, label %ok
closedir:
  %c1 = call i32 @close(i32 %fd)
  ret i32 -5
closeother:
  %c2 = call i32 @close(i32 %fd)
  ret i32 -1
ok:
  ret i32 %fd
invalid:
  ret i32 -6
}

; Sys.FileRead(int h, Span<byte> buf) -> long
define internal i64 @__vib_sys_file_read(i32 %fd, ptr noalias %buf.ptr, i32 %buf.len) nounwind {
entry:
  %n = zext i32 %buf.len to i64
  br label %try
try:
  %r = call i64 @read(i32 %fd, ptr %buf.ptr, i64 %n)
  %failed = icmp slt i64 %r, 0
  br i1 %failed, label %err, label %ok
err:
  %e = call i32 @__vib_sys_errno()
  %intr = icmp eq i32 %e, 4
  br i1 %intr, label %try, label %fail
fail:
  %k = call i32 @__vib_sys_kind_fs(i32 %e)
  %k64 = sext i32 %k to i64
  %neg = sub i64 0, %k64
  ret i64 %neg
ok:
  ret i64 %r
}

; Sys.FileWriteAll(int h, ReadOnlySpan<byte> data) -> int
define internal i32 @__vib_sys_file_write_all(i32 %fd, ptr readonly %p, i32 %len) nounwind {
entry:
  %n = zext i32 %len to i64
  br label %loop
loop:
  %off = phi i64 [ 0, %entry ], [ %off.next, %advance ], [ %off, %err ]
  %done = icmp uge i64 %off, %n
  br i1 %done, label %ok, label %body
body:
  %q = getelementptr i8, ptr %p, i64 %off
  %rem = sub i64 %n, %off
  %w = call i64 @write(i32 %fd, ptr %q, i64 %rem)
  %neg = icmp slt i64 %w, 0
  br i1 %neg, label %err, label %progress
progress:
  %zero = icmp eq i64 %w, 0
  br i1 %zero, label %writezero, label %advance
advance:
  %off.next = add i64 %off, %w
  br label %loop
err:
  %e = call i32 @__vib_sys_errno()
  %intr = icmp eq i32 %e, 4
  br i1 %intr, label %loop, label %fail
fail:
  %k = call i32 @__vib_sys_kind_fs(i32 %e)
  %nk = sub i32 0, %k
  ret i32 %nk
writezero:
  ret i32 -9
ok:
  ret i32 0
}

; Sys.FileSync(int h) -> int
define internal i32 @__vib_sys_file_sync(i32 %fd) nounwind {
entry:
  br label %try
try:
  %r = call i32 @fsync(i32 %fd)
  %failed = icmp slt i32 %r, 0
  br i1 %failed, label %err, label %ok
err:
  %e = call i32 @__vib_sys_errno()
  %intr = icmp eq i32 %e, 4
  br i1 %intr, label %try, label %fail
fail:
  %k = call i32 @__vib_sys_kind_fs(i32 %e)
  %nk = sub i32 0, %k
  ret i32 %nk
ok:
  ret i32 0
}

; Sys.FileExists(ReadOnlySpan<byte> path) -> int
define internal i32 @__vib_sys_file_exists(ptr readonly %path.ptr, i32 %path.len) nounwind {
entry:
  %buf = alloca [4096 x i8], align 1
  %st = alloca [144 x i8], align 8
  %nonpos = icmp sle i32 %path.len, 0
  %toolong = icmp ugt i32 %path.len, 4095
  %badlen = or i1 %nonpos, %toolong
  br i1 %badlen, label %nope, label %scan_nul
scan_nul:
  %i = phi i32 [ 0, %entry ], [ %i.next, %scan_cont ]
  %scan_done = icmp sge i32 %i, %path.len
  br i1 %scan_done, label %copy, label %scan_body
scan_body:
  %i64 = zext i32 %i to i64
  %bp = getelementptr inbounds i8, ptr %path.ptr, i64 %i64
  %b = load i8, ptr %bp, align 1
  %has_nul = icmp eq i8 %b, 0
  br i1 %has_nul, label %nope, label %scan_cont
scan_cont:
  %i.next = add i32 %i, 1
  br label %scan_nul
copy:
  %len64 = zext i32 %path.len to i64
  call void @llvm.memcpy.p0.p0.i64(ptr %buf, ptr %path.ptr, i64 %len64, i1 false)
  %nul = getelementptr inbounds i8, ptr %buf, i64 %len64
  store i8 0, ptr %nul
  %lr = call i32 @lstat(ptr %buf, ptr %st)
  %lok = icmp eq i32 %lr, 0
  br i1 %lok, label %check_lmode, label %nope
check_lmode:
  %lm.p = getelementptr inbounds i8, ptr %st, i64 24
  %lm = load i32, ptr %lm.p, align 4
  %lt = and i32 %lm, 61440
  %islnk = icmp eq i32 %lt, 40960
  br i1 %islnk, label %follow_lnk, label %check_notdir_l
follow_lnk:
  %sr = call i32 @stat(ptr %buf, ptr %st)
  %sok = icmp eq i32 %sr, 0
  br i1 %sok, label %check_smode, label %yes
check_smode:
  %sm.p = getelementptr inbounds i8, ptr %st, i64 24
  %sm = load i32, ptr %sm.p, align 4
  %st_t = and i32 %sm, 61440
  %notdir_s = icmp ne i32 %st_t, 16384
  %res_s = zext i1 %notdir_s to i32
  ret i32 %res_s
check_notdir_l:
  %notdir_l = icmp ne i32 %lt, 16384
  %res_l = zext i1 %notdir_l to i32
  ret i32 %res_l
yes:
  ret i32 1
nope:
  ret i32 0
}

; Sys.Close(int h) -> void
define internal void @__vib_sys_close(i32 %fd) nounwind {
entry:
  %r = call i32 @close(i32 %fd)
  ret void
}

; Sys.StdinRead(Span<byte> buf) -> long
define internal i64 @__vib_sys_stdin_read(ptr noalias %buf.ptr, i32 %buf.len) nounwind {
entry:
  %nonpos = icmp sle i32 %buf.len, 0
  br i1 %nonpos, label %empty, label %prep
empty:
  ret i64 0
prep:
  %n = zext i32 %buf.len to i64
  br label %try
try:
  %r = call i64 @read(i32 0, ptr %buf.ptr, i64 %n)
  %failed = icmp slt i64 %r, 0
  br i1 %failed, label %err, label %ok
err:
  %e = call i32 @__vib_sys_errno()
  %intr = icmp eq i32 %e, 4
  br i1 %intr, label %try, label %fail
fail:
  %k = call i32 @__vib_sys_kind_fs(i32 %e)
  %k64 = sext i32 %k to i64
  %neg = sub i64 0, %k64
  ret i64 %neg
ok:
  ret i64 %r
}

; =============================== Net =========================================
define internal void @__vib_sys_fill_sockaddr(ptr %sa, i32 %addr, i32 %port) nounwind {
entry:
  call void @llvm.memset.p0.i64(ptr %sa, i8 0, i64 16, i1 false)
  store i16 2, ptr %sa, align 2
  %port.p = getelementptr inbounds i8, ptr %sa, i64 2
  %p16 = trunc i32 %port to i16
  %pbe = call i16 @llvm.bswap.i16(i16 %p16)
  store i16 %pbe, ptr %port.p, align 2
  %addr.p = getelementptr inbounds i8, ptr %sa, i64 4
  %abe = call i32 @llvm.bswap.i32(i32 %addr)
  store i32 %abe, ptr %addr.p, align 4
  ret void
}

; Sys.TcpListen(int addr, int port, int backlog) -> int
define internal i32 @__vib_sys_tcp_listen(i32 %addr, i32 %port, i32 %backlog) nounwind {
entry:
  %sa = alloca [16 x i8], align 4
  %one = alloca i32, align 4
  %fd = call i32 @socket(i32 2, i32 524289, i32 0)
  %bad = icmp slt i32 %fd, 0
  br i1 %bad, label %nofd, label %opt
opt:
  store i32 1, ptr %one
  %r1 = call i32 @setsockopt(i32 %fd, i32 1, i32 2, ptr %one, i32 4)
  %b1 = icmp slt i32 %r1, 0
  br i1 %b1, label %failclose, label %dobind
dobind:
  call void @__vib_sys_fill_sockaddr(ptr %sa, i32 %addr, i32 %port)
  %r2 = call i32 @bind(i32 %fd, ptr %sa, i32 16)
  %b2 = icmp slt i32 %r2, 0
  br i1 %b2, label %failclose, label %dolisten
dolisten:
  %r3 = call i32 @listen(i32 %fd, i32 %backlog)
  %b3 = icmp slt i32 %r3, 0
  br i1 %b3, label %failclose, label %ok
ok:
  ret i32 %fd
failclose:
  %e = call i32 @__vib_sys_errno()
  %c = call i32 @close(i32 %fd)
  %k = call i32 @__vib_sys_kind_net(i32 %e)
  %nk = sub i32 0, %k
  ret i32 %nk
nofd:
  %e0 = call i32 @__vib_sys_errno()
  %k0 = call i32 @__vib_sys_kind_net(i32 %e0)
  %nk0 = sub i32 0, %k0
  ret i32 %nk0
}

; Sys.TcpAccept(int listener) -> int
define internal i32 @__vib_sys_tcp_accept(i32 %fd) nounwind {
entry:
  br label %try
try:
  %c = call i32 @accept4(i32 %fd, ptr null, ptr null, i32 524288)
  %failed = icmp slt i32 %c, 0
  br i1 %failed, label %err, label %ok
err:
  %e = call i32 @__vib_sys_errno()
  %intr = icmp eq i32 %e, 4
  br i1 %intr, label %try, label %fail
fail:
  %k = call i32 @__vib_sys_kind_net(i32 %e)
  %nk = sub i32 0, %k
  ret i32 %nk
ok:
  ret i32 %c
}

; Sys.TcpConnect(int addr, int port) -> int
define internal i32 @__vib_sys_tcp_connect(i32 %addr, i32 %port) nounwind {
entry:
  %sa = alloca [16 x i8], align 4
  %fd = call i32 @socket(i32 2, i32 524289, i32 0)
  %bad = icmp slt i32 %fd, 0
  br i1 %bad, label %nofd, label %go
go:
  call void @__vib_sys_fill_sockaddr(ptr %sa, i32 %addr, i32 %port)
  %r = call i32 @connect(i32 %fd, ptr %sa, i32 16)
  %failed = icmp slt i32 %r, 0
  br i1 %failed, label %failclose, label %ok
ok:
  ret i32 %fd
failclose:
  %e = call i32 @__vib_sys_errno()
  %c = call i32 @close(i32 %fd)
  %k = call i32 @__vib_sys_kind_net(i32 %e)
  %nk = sub i32 0, %k
  ret i32 %nk
nofd:
  %e0 = call i32 @__vib_sys_errno()
  %k0 = call i32 @__vib_sys_kind_net(i32 %e0)
  %nk0 = sub i32 0, %k0
  ret i32 %nk0
}

; Sys.SockRecv(int h, Span<byte> buf) -> long
define internal i64 @__vib_sys_sock_recv(i32 %fd, ptr noalias %buf.ptr, i32 %buf.len) nounwind {
entry:
  %n = zext i32 %buf.len to i64
  br label %try
try:
  %r = call i64 @recv(i32 %fd, ptr %buf.ptr, i64 %n, i32 0)
  %failed = icmp slt i64 %r, 0
  br i1 %failed, label %err, label %ok
err:
  %e = call i32 @__vib_sys_errno()
  %intr = icmp eq i32 %e, 4
  br i1 %intr, label %try, label %fail
fail:
  %k = call i32 @__vib_sys_kind_net(i32 %e)
  %k64 = sext i32 %k to i64
  %neg = sub i64 0, %k64
  ret i64 %neg
ok:
  ret i64 %r
}

; Sys.SockSendAll(int h, ReadOnlySpan<byte> data) -> int
define internal i32 @__vib_sys_sock_send_all(i32 %fd, ptr readonly %p, i32 %len) nounwind {
entry:
  %n = zext i32 %len to i64
  br label %loop
loop:
  %off = phi i64 [ 0, %entry ], [ %off.next, %advance ], [ %off, %err ]
  %done = icmp uge i64 %off, %n
  br i1 %done, label %ok, label %body
body:
  %q = getelementptr i8, ptr %p, i64 %off
  %rem = sub i64 %n, %off
  %w = call i64 @send(i32 %fd, ptr %q, i64 %rem, i32 16384)
  %neg = icmp slt i64 %w, 0
  br i1 %neg, label %err, label %progress
progress:
  %zero = icmp eq i64 %w, 0
  br i1 %zero, label %writezero, label %advance
advance:
  %off.next = add i64 %off, %w
  br label %loop
err:
  %e = call i32 @__vib_sys_errno()
  %intr = icmp eq i32 %e, 4
  br i1 %intr, label %loop, label %fail
fail:
  %k = call i32 @__vib_sys_kind_net(i32 %e)
  %nk = sub i32 0, %k
  ret i32 %nk
writezero:
  ret i32 -9
ok:
  ret i32 0
}

; Sys.SockShutdownWrite(int h) -> int
define internal i32 @__vib_sys_sock_shutdown_write(i32 %fd) nounwind {
entry:
  %r = call i32 @shutdown(i32 %fd, i32 1)
  %failed = icmp slt i32 %r, 0
  br i1 %failed, label %fail, label %ok
fail:
  %e = call i32 @__vib_sys_errno()
  %k = call i32 @__vib_sys_kind_net(i32 %e)
  %nk = sub i32 0, %k
  ret i32 %nk
ok:
  ret i32 0
}

; Sys.SockLocalPort(int h) -> int
define internal i32 @__vib_sys_sock_local_port(i32 %fd) nounwind {
entry:
  %sa = alloca [16 x i8], align 4
  %len = alloca i32, align 4
  store i32 16, ptr %len
  %r = call i32 @getsockname(i32 %fd, ptr %sa, ptr %len)
  %failed = icmp slt i32 %r, 0
  br i1 %failed, label %fail, label %ok
fail:
  %e = call i32 @__vib_sys_errno()
  %k = call i32 @__vib_sys_kind_net(i32 %e)
  %nk = sub i32 0, %k
  ret i32 %nk
ok:
  %port.p = getelementptr inbounds i8, ptr %sa, i64 2
  %pbe = load i16, ptr %port.p, align 2
  %p = call i16 @llvm.bswap.i16(i16 %pbe)
  %p32 = zext i16 %p to i32
  ret i32 %p32
}

; =============================== Process =====================================
; Sys.SpawnWait(ReadOnlySpan<byte> arena, ReadOnlySpan<int> starts) -> int
;   memory-relevant preconditions are re-checked here (arena NUL-terminated, every start in range)
define internal i32 @__vib_sys_spawn_wait(ptr readonly %arena.ptr, i32 %arena.len, ptr readonly %starts.ptr, i32 %starts.len) nounwind {
entry:
  %pid = alloca i32, align 4
  %status = alloca i32, align 4
  %none = icmp slt i32 %starts.len, 1
  %empty = icmp slt i32 %arena.len, 1
  %bad0 = or i1 %none, %empty
  br i1 %bad0, label %invalid, label %termcheck
termcheck:
  %alen64 = zext i32 %arena.len to i64
  %lastoff = sub i64 %alen64, 1
  %last.p = getelementptr inbounds i8, ptr %arena.ptr, i64 %lastoff
  %lastb = load i8, ptr %last.p, align 1
  %terminated = icmp eq i8 %lastb, 0
  br i1 %terminated, label %alloc, label %invalid
alloc:
  %n64 = zext i32 %starts.len to i64
  %slots = add i64 %n64, 1
  %bytes = shl i64 %slots, 3
  %argv = call ptr @malloc(i64 %bytes)
  %isnull = icmp eq ptr %argv, null
  br i1 %isnull, label %oom, label %fill
fill:
  %i = phi i64 [ 0, %alloc ], [ %i.next, %fillstore ]
  %more = icmp ult i64 %i, %n64
  br i1 %more, label %fillbody, label %spawn
fillbody:
  %s.p = getelementptr inbounds i32, ptr %starts.ptr, i64 %i
  %s = load i32, ptr %s.p, align 4
  %inrange = icmp ult i32 %s, %arena.len
  br i1 %inrange, label %fillstore, label %badstart
fillstore:
  %s64 = zext i32 %s to i64
  %str = getelementptr inbounds i8, ptr %arena.ptr, i64 %s64
  %slot = getelementptr inbounds ptr, ptr %argv, i64 %i
  store ptr %str, ptr %slot, align 8
  %i.next = add i64 %i, 1
  br label %fill
spawn:
  %last = getelementptr inbounds ptr, ptr %argv, i64 %n64
  store ptr null, ptr %last, align 8
  %prog = load ptr, ptr %argv, align 8
  %env = load ptr, ptr @environ, align 8
  %rc = call i32 @posix_spawn(ptr %pid, ptr %prog, ptr null, ptr null, ptr %argv, ptr %env)
  call void @free(ptr %argv)
  %spawned = icmp eq i32 %rc, 0
  br i1 %spawned, label %wait, label %spawnfail
spawnfail:
  %isnoent = icmp eq i32 %rc, 2
  %isnotdir = icmp eq i32 %rc, 20
  %nf = or i1 %isnoent, %isnotdir
  %isacces = icmp eq i32 %rc, 13
  %k1 = select i1 %isacces, i32 -3, i32 -1
  %k = select i1 %nf, i32 -2, i32 %k1
  ret i32 %k
wait:
  %child = load i32, ptr %pid, align 4
  %w = call i32 @waitpid(i32 %child, ptr %status, i32 0)
  %wfail = icmp slt i32 %w, 0
  br i1 %wfail, label %waiterr, label %decode
waiterr:
  %e = call i32 @__vib_sys_errno()
  %intr = icmp eq i32 %e, 4
  br i1 %intr, label %wait, label %other
decode:
  %st = load i32, ptr %status, align 4
  %sig = and i32 %st, 127
  %exited = icmp eq i32 %sig, 0
  %hi = lshr i32 %st, 8
  %code = and i32 %hi, 255
  %sigcode = add i32 %sig, 128
  %res = select i1 %exited, i32 %code, i32 %sigcode
  ret i32 %res
other:
  ret i32 -1
badstart:
  call void @free(ptr %argv)
  ret i32 -6
invalid:
  ret i32 -6
oom:
  call void @__vib_panic(ptr @.rt.oom, i64 13)
  unreachable
}

; =============================== Env =========================================
; Sys.ArgCount() -> int
define internal i32 @__vib_sys_arg_count() nounwind {
entry:
  %argc = load i32, ptr @__vib_argc
  %n = sub i32 %argc, 1
  %neg = icmp slt i32 %n, 0
  %r = select i1 %neg, i32 0, i32 %n
  ret i32 %r
}

; Sys.ArgCopy(int index, Span<byte> dst) -> int
define internal i32 @__vib_sys_arg_copy(i32 %index, ptr noalias %dst.ptr, i32 %dst.len) nounwind {
entry:
  %count = call i32 @__vib_sys_arg_count()
  %oob = icmp uge i32 %index, %count
  br i1 %oob, label %invalid, label %go
go:
  %argv = load ptr, ptr @__vib_argv
  %i1 = add i32 %index, 1
  %i64 = zext i32 %i1 to i64
  %slot = getelementptr inbounds ptr, ptr %argv, i64 %i64
  %s = load ptr, ptr %slot, align 8
  %len = call i64 @strlen(ptr %s)
  %toobig = icmp ugt i64 %len, 2147483591
  br i1 %toobig, label %invalid, label %copy
copy:
  %negcap = icmp slt i32 %dst.len, 0
  br i1 %negcap, label %invalid, label %docheck
docheck:
  %cap = zext i32 %dst.len to i64
  %fits = icmp ule i64 %len, %cap
  %n = select i1 %fits, i64 %len, i64 %cap
  %nzero = icmp eq i64 %n, 0
  br i1 %nzero, label %retlen, label %domemcpy
domemcpy:
  call void @llvm.memcpy.p0.p0.i64(ptr %dst.ptr, ptr %s, i64 %n, i1 false)
  br label %retlen
retlen:
  %len32 = trunc i64 %len to i32
  ret i32 %len32
invalid:
  ret i32 -6
}
