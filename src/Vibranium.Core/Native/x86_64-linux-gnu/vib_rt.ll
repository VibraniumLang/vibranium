; =============================================================================
; vib_rt.ll  --  Vibranium.Core native runtime fragment (v0.2)
; Target: x86_64-pc-linux-gnu (glibc >= 2.33), LLVM 18 opaque pointers (Zero C).
; Prepended verbatim by vib; user module defines `internal i32 @__vib_entry()`.
; =============================================================================
target datalayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128"
target triple = "x86_64-pc-linux-gnu"

%vib.vec = type { ptr, i32, i32 }

declare noalias ptr @malloc(i64)
declare noalias ptr @realloc(ptr, i64)
declare void @free(ptr)
declare i32 @memcmp(ptr nocapture readonly, ptr nocapture readonly, i64)
declare i64 @write(i32, ptr nocapture readonly, i64)
declare void @_exit(i32) noreturn nounwind
declare ptr @signal(i32, ptr)
declare ptr @__errno_location()
declare void @llvm.memcpy.p0.p0.i64(ptr noalias nocapture writeonly, ptr noalias nocapture readonly, i64, i1 immarg)
declare void @llvm.memset.p0.i64(ptr nocapture writeonly, i8, i64, i1 immarg)
declare { i32, i1 } @llvm.sadd.with.overflow.i32(i32, i32)
declare { i32, i1 } @llvm.ssub.with.overflow.i32(i32, i32)
declare { i32, i1 } @llvm.smul.with.overflow.i32(i32, i32)
declare { i64, i1 } @llvm.sadd.with.overflow.i64(i64, i64)
declare { i64, i1 } @llvm.ssub.with.overflow.i64(i64, i64)
declare { i64, i1 } @llvm.smul.with.overflow.i64(i64, i64)
declare { i64, i1 } @llvm.umul.with.overflow.i64(i64, i64)
declare i8 @llvm.fptosi.sat.i8.f32(float)
declare i8 @llvm.fptosi.sat.i8.f64(double)
declare i8 @llvm.fptoui.sat.i8.f32(float)
declare i8 @llvm.fptoui.sat.i8.f64(double)
declare i16 @llvm.fptosi.sat.i16.f32(float)
declare i16 @llvm.fptosi.sat.i16.f64(double)
declare i16 @llvm.fptoui.sat.i16.f32(float)
declare i16 @llvm.fptoui.sat.i16.f64(double)
declare i32 @llvm.fptosi.sat.i32.f32(float)
declare i32 @llvm.fptosi.sat.i32.f64(double)
declare i32 @llvm.fptoui.sat.i32.f32(float)
declare i32 @llvm.fptoui.sat.i32.f64(double)
declare i64 @llvm.fptosi.sat.i64.f32(float)
declare i64 @llvm.fptosi.sat.i64.f64(double)
declare i64 @llvm.fptoui.sat.i64.f32(float)
declare i64 @llvm.fptoui.sat.i64.f64(double)

@__vib_argc = internal global i32 0
@__vib_argv = internal global ptr null

@.str.empty = private unnamed_addr constant [1 x i8] zeroinitializer
@.b_empty   = private unnamed_addr constant [1 x i8] zeroinitializer
@.rt.panic  = private unnamed_addr constant [7 x i8] c"panic: "
@.rt.nl     = private unnamed_addr constant [1 x i8] c"\0A"
@.rt.true   = private unnamed_addr constant [4 x i8] c"True"
@.rt.false  = private unnamed_addr constant [5 x i8] c"False"
@.rt.oom    = private unnamed_addr constant [13 x i8] c"out of memory"
@.rt.capov  = private unnamed_addr constant [21 x i8] c"Vec capacity overflow"
@.rt.neglen = private unnamed_addr constant [29 x i8] c"Vec length cannot be negative"

define i32 @main(i32 %argc, ptr %argv) nounwind {
entry:
  store i32 %argc, ptr @__vib_argc
  store ptr %argv, ptr @__vib_argv
  %old = call ptr @signal(i32 13, ptr inttoptr (i64 1 to ptr))
  %rc = call i32 @__vib_entry()
  ret i32 %rc
}

; Exact truncating IEEE-754 remainder without an LLVM frem/libm libcall.
; Independently derived binary long division: |x| = mx * 2^(ex-1075),
; |y| = my * 2^(ey-1075), with 2^52 <= mx,my < 2^53. Reduce mx
; by my, then shift the remainder once for each remaining exponent bit.
; Each iteration starts below 2*my (< 2^54); at most 2098 reductions
; cover the entire binary64 exponent range, including subnormal inputs.
; No floating quotient, integer quotient conversion, or rounding is used.
; NaN payload/sign and floating environment flags are not part of this API.
declare i64 @llvm.ctlz.i64(i64, i1 immarg)

define internal double @__vib_rem_f64(double %x, double %y) nounwind willreturn memory(none) {
entry:
  %xb = bitcast double %x to i64
  %yb = bitcast double %y to i64
  %sign = and i64 %xb, -9223372036854775808
  %ax = and i64 %xb, 9223372036854775807
  %ay = and i64 %yb, 9223372036854775807
  %x.invalid = icmp uge i64 %ax, 9218868437227405312
  %y.nan = icmp ugt i64 %ay, 9218868437227405312
  %y.zero = icmp eq i64 %ay, 0
  %invalid.xy = or i1 %x.invalid, %y.nan
  %invalid = or i1 %invalid.xy, %y.zero
  br i1 %invalid, label %nan, label %compare
nan:
  ret double 0x7FF8000000000000
compare:
  ; This also returns finite x unchanged for infinite y, and preserves -0.
  %less = icmp ult i64 %ax, %ay
  br i1 %less, label %unchanged, label %compare.equal
unchanged:
  ret double %x
compare.equal:
  %equal = icmp eq i64 %ax, %ay
  br i1 %equal, label %zero, label %decode
zero:
  %signed.zero = bitcast i64 %sign to double
  ret double %signed.zero
decode:
  ; Both magnitudes are now finite and nonzero. ctlz cannot see zero.
  %xe = lshr i64 %ax, 52
  %ye = lshr i64 %ay, 52
  %xf = and i64 %ax, 4503599627370495
  %yf = and i64 %ay, 4503599627370495
  %xn = or i64 %xf, 4503599627370496
  %yn = or i64 %yf, 4503599627370496
  %xs = icmp eq i64 %xe, 0
  %ys = icmp eq i64 %ye, 0
  %xm = select i1 %xs, i64 %xf, i64 %xn
  %ym = select i1 %ys, i64 %yf, i64 %yn
  %xe.base = select i1 %xs, i64 1, i64 %xe
  %ye.base = select i1 %ys, i64 1, i64 %ye
  %xz = call i64 @llvm.ctlz.i64(i64 %xm, i1 false)
  %yz = call i64 @llvm.ctlz.i64(i64 %ym, i1 false)
  %xshift = sub i64 %xz, 11
  %yshift = sub i64 %yz, 11
  ; Normalization shifts range from 0 to 52, exponents from -51 to 2046.
  %mx = shl i64 %xm, %xshift
  %my = shl i64 %ym, %yshift
  %ex = sub i64 %xe.base, %xshift
  %ey = sub i64 %ye.base, %yshift
  br label %reduce
reduce:
  %r = phi i64 [ %mx, %decode ], [ %next.r, %advance ]
  %e = phi i64 [ %ex, %decode ], [ %next.e, %advance ]
  %ge = icmp uge i64 %r, %my
  %difference = sub i64 %r, %my
  %reduced = select i1 %ge, i64 %difference, i64 %r
  %is.zero = icmp eq i64 %reduced, 0
  br i1 %is.zero, label %zero, label %test.exponent
test.exponent:
  %at.divisor = icmp eq i64 %e, %ey
  br i1 %at.divisor, label %normalize, label %advance
advance:
  %next.r = shl i64 %reduced, 1
  %next.e = sub i64 %e, 1
  br label %reduce
normalize:
  %rz = call i64 @llvm.ctlz.i64(i64 %reduced, i1 false)
  %rshift = sub i64 %rz, 11
  %rm = shl i64 %reduced, %rshift
  %re = sub i64 %ey, %rshift
  %subnormal = icmp sle i64 %re, 0
  br i1 %subnormal, label %pack.subnormal, label %pack.normal
pack.subnormal:
  ; The exact remainder is a multiple of the smaller operand's quantum,
  ; never finer than 2^-1074. Thus 1-re <= 52 and discarded bits are zero.
  %pack.shift = sub i64 1, %re
  %sub.bits = lshr i64 %rm, %pack.shift
  br label %pack.sign
pack.normal:
  %exp.bits = shl i64 %re, 52
  %fraction.bits = and i64 %rm, 4503599627370495
  %normal.bits = or i64 %exp.bits, %fraction.bits
  br label %pack.sign
pack.sign:
  %magnitude = phi i64 [ %sub.bits, %pack.subnormal ], [ %normal.bits, %pack.normal ]
  %result.bits = or i64 %magnitude, %sign
  %result = bitcast i64 %result.bits to double
  ret double %result
}

; Widening binary32 is exact. Its remainder has at most 24 significant
; bits at a scale no finer than 2^-149, so narrowing is also exact, including
; subnormals and signed zero. NaN classification is preserved, not payloads.
define internal float @__vib_rem_f32(float %x, float %y) nounwind willreturn memory(none) {
entry:
  %wide.x = fpext float %x to double
  %wide.y = fpext float %y to double
  %wide.result = call double @__vib_rem_f64(double %wide.x, double %wide.y)
  %result = fptrunc double %wide.result to float
  ret float %result
}

define internal void @__vib_write_all(i32 %fd, ptr readonly %p, i64 %n) nounwind {
entry:
  br label %loop
loop:
  %off = phi i64 [ 0, %entry ], [ %off.next, %advance ], [ %off, %failed ]
  %done = icmp sge i64 %off, %n
  br i1 %done, label %exit, label %body
body:
  %q = getelementptr i8, ptr %p, i64 %off
  %rem = sub i64 %n, %off
  %w = call i64 @write(i32 %fd, ptr %q, i64 %rem)
  %ok = icmp sgt i64 %w, 0
  br i1 %ok, label %advance, label %failed
advance:
  %off.next = add i64 %off, %w
  br label %loop
failed:
  %ep = call ptr @__errno_location()
  %e = load i32, ptr %ep
  %neg = icmp slt i64 %w, 0
  %intr = icmp eq i32 %e, 4
  %retry = and i1 %neg, %intr
  br i1 %retry, label %loop, label %exit
exit:
  ret void
}

define internal void @__vib_write_out(ptr readonly %p, i32 %len) nounwind {
entry:
  %pos = icmp sgt i32 %len, 0
  br i1 %pos, label %go, label %done
go:
  %n = zext i32 %len to i64
  call void @__vib_write_all(i32 1, ptr %p, i64 %n)
  br label %done
done:
  ret void
}

define internal void @__vib_write_err(ptr readonly %p, i32 %len) nounwind {
entry:
  %pos = icmp sgt i32 %len, 0
  br i1 %pos, label %go, label %done
go:
  %n = zext i32 %len to i64
  call void @__vib_write_all(i32 2, ptr %p, i64 %n)
  br label %done
done:
  ret void
}

define internal void @__vib_panic(ptr readonly %msg_ptr, i64 %msg_len) noreturn cold nounwind {
entry:
  call void @__vib_write_all(i32 2, ptr @.rt.panic, i64 7)
  %has_msg = icmp sgt i64 %msg_len, 0
  br i1 %has_msg, label %write_msg, label %write_nl
write_msg:
  call void @__vib_write_all(i32 2, ptr %msg_ptr, i64 %msg_len)
  br label %write_nl
write_nl:
  call void @__vib_write_all(i32 2, ptr @.rt.nl, i64 1)
  call void @_exit(i32 101)
  unreachable
}

define internal void @__vib_print_i64(i64 %val) nounwind {
entry:
  %buf = alloca [24 x i8], align 1
  %is_neg = icmp slt i64 %val, 0
  %neg_val = sub i64 0, %val
  %mag_init = select i1 %is_neg, i64 %neg_val, i64 %val
  br label %digit_loop
digit_loop:
  %cur_mag = phi i64 [ %mag_init, %entry ], [ %next_mag, %digit_loop ]
  %cur_pos = phi i64 [ 24, %entry ], [ %next_pos, %digit_loop ]
  %rem = urem i64 %cur_mag, 10
  %next_mag = udiv i64 %cur_mag, 10
  %digit_i8 = trunc i64 %rem to i8
  %ascii = add i8 %digit_i8, 48
  %next_pos = sub i64 %cur_pos, 1
  %slot = getelementptr inbounds [24 x i8], ptr %buf, i64 0, i64 %next_pos
  store i8 %ascii, ptr %slot, align 1
  %more = icmp ne i64 %next_mag, 0
  br i1 %more, label %digit_loop, label %check_sign
check_sign:
  br i1 %is_neg, label %emit_minus, label %flush
emit_minus:
  %minus_pos = sub i64 %next_pos, 1
  %mslot = getelementptr inbounds [24 x i8], ptr %buf, i64 0, i64 %minus_pos
  store i8 45, ptr %mslot, align 1
  br label %flush
flush:
  %start_pos = phi i64 [ %next_pos, %check_sign ], [ %minus_pos, %emit_minus ]
  %len = sub i64 24, %start_pos
  %out_ptr = getelementptr inbounds [24 x i8], ptr %buf, i64 0, i64 %start_pos
  call void @__vib_write_all(i32 1, ptr %out_ptr, i64 %len)
  ret void
}

define internal void @__vib_print_u64(i64 %val) nounwind {
entry:
  %buf = alloca [24 x i8], align 1
  br label %digit_loop
digit_loop:
  %cur_mag = phi i64 [ %val, %entry ], [ %next_mag, %digit_loop ]
  %cur_pos = phi i64 [ 24, %entry ], [ %next_pos, %digit_loop ]
  %rem = urem i64 %cur_mag, 10
  %next_mag = udiv i64 %cur_mag, 10
  %digit_i8 = trunc i64 %rem to i8
  %ascii = add i8 %digit_i8, 48
  %next_pos = sub i64 %cur_pos, 1
  %slot = getelementptr inbounds [24 x i8], ptr %buf, i64 0, i64 %next_pos
  store i8 %ascii, ptr %slot, align 1
  %more = icmp ne i64 %next_mag, 0
  br i1 %more, label %digit_loop, label %flush
flush:
  %len = sub i64 24, %next_pos
  %out_ptr = getelementptr inbounds [24 x i8], ptr %buf, i64 0, i64 %next_pos
  call void @__vib_write_all(i32 1, ptr %out_ptr, i64 %len)
  ret void
}

define internal void @__vib_print_bool(i1 %val) nounwind {
entry:
  br i1 %val, label %is_true, label %is_false
is_true:
  call void @__vib_write_all(i32 1, ptr @.rt.true, i64 4)
  ret void
is_false:
  call void @__vib_write_all(i32 1, ptr @.rt.false, i64 5)
  ret void
}

define internal void @__vib_print_str(ptr readonly %p, i64 %len) nounwind {
entry:
  %non_empty = icmp sgt i64 %len, 0
  br i1 %non_empty, label %do_write, label %done
do_write:
  call void @__vib_write_all(i32 1, ptr %p, i64 %len)
  br label %done
done:
  ret void
}

define internal void @__vib_print_nl() nounwind {
entry:
  call void @__vib_write_all(i32 1, ptr @.rt.nl, i64 1)
  ret void
}

define internal void @__vib_wl_push(ptr %wl, ptr %obj, ptr %step_fn) nounwind {
entry:
  %is_null = icmp eq ptr %obj, null
  br i1 %is_null, label %done, label %check_cap
check_cap:
  %dp_ptr = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 0
  %len_ptr = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 1
  %cap_ptr = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 2
  %heap_ptr = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 3
  %len = load i64, ptr %len_ptr
  %cap = load i64, ptr %cap_ptr
  %full = icmp eq i64 %len, %cap
  br i1 %full, label %grow, label %push_item
grow:
  %old_data = load ptr, ptr %dp_ptr
  %is_heap = load i1, ptr %heap_ptr
  %new_cap = shl i64 %cap, 1
  %new_bytes = shl i64 %new_cap, 4
  br i1 %is_heap, label %grow_realloc, label %grow_malloc
grow_realloc:
  %re_ptr = call noalias ptr @realloc(ptr %old_data, i64 %new_bytes)
  br label %grow_check
grow_malloc:
  %ma_ptr = call noalias ptr @malloc(i64 %new_bytes)
  %ma_ok = icmp ne ptr %ma_ptr, null
  br i1 %ma_ok, label %grow_copy, label %grow_oom
grow_copy:
  %old_bytes = shl i64 %cap, 4
  call void @llvm.memcpy.p0.p0.i64(ptr %ma_ptr, ptr %old_data, i64 %old_bytes, i1 false)
  store i1 true, ptr %heap_ptr
  br label %grow_check
grow_check:
  %grown_ptr = phi ptr [ %re_ptr, %grow_realloc ], [ %ma_ptr, %grow_copy ]
  %oom = icmp eq ptr %grown_ptr, null
  br i1 %oom, label %grow_oom, label %grow_done
grow_oom:
  call void @__vib_panic(ptr @.rt.oom, i64 13)
  unreachable
grow_done:
  store ptr %grown_ptr, ptr %dp_ptr
  store i64 %new_cap, ptr %cap_ptr
  br label %push_item
push_item:
  %cur_data = load ptr, ptr %dp_ptr
  %slot_ptr = getelementptr inbounds { ptr, ptr }, ptr %cur_data, i64 %len
  %s0 = getelementptr inbounds { ptr, ptr }, ptr %slot_ptr, i32 0, i32 0
  %s1 = getelementptr inbounds { ptr, ptr }, ptr %slot_ptr, i32 0, i32 1
  store ptr %obj, ptr %s0
  store ptr %step_fn, ptr %s1
  %next_len = add i64 %len, 1
  store i64 %next_len, ptr %len_ptr
  br label %done
done:
  ret void
}

define internal void @__vib_wl_drain(ptr %wl) nounwind {
entry:
  %dp_ptr = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 0
  %len_ptr = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 1
  %heap_ptr = getelementptr inbounds { ptr, i64, i64, i1 }, ptr %wl, i32 0, i32 3
  br label %loop_cond
loop_cond:
  %len = load i64, ptr %len_ptr
  %has_more = icmp ugt i64 %len, 0
  br i1 %has_more, label %pop_item, label %cleanup
pop_item:
  %top = sub i64 %len, 1
  store i64 %top, ptr %len_ptr
  %data = load ptr, ptr %dp_ptr
  %item_ptr = getelementptr inbounds { ptr, ptr }, ptr %data, i64 %top
  %p0 = getelementptr inbounds { ptr, ptr }, ptr %item_ptr, i32 0, i32 0
  %p1 = getelementptr inbounds { ptr, ptr }, ptr %item_ptr, i32 0, i32 1
  %obj = load ptr, ptr %p0
  %step_fn = load ptr, ptr %p1
  call void %step_fn(ptr %obj, ptr %wl)
  br label %loop_cond
cleanup:
  %is_heap = load i1, ptr %heap_ptr
  br i1 %is_heap, label %free_wl, label %done
free_wl:
  %final_data = load ptr, ptr %dp_ptr
  call void @free(ptr %final_data)
  br label %done
done:
  ret void
}

define internal void @__vib_vec_reserve(ptr noalias %v, i32 %additional, i64 %elem) nounwind {
entry:
  %cnt.p = getelementptr inbounds %vib.vec, ptr %v, i32 0, i32 1
  %cap.p = getelementptr inbounds %vib.vec, ptr %v, i32 0, i32 2
  %cnt = load i32, ptr %cnt.p
  %cap = load i32, ptr %cap.p
  %neg = icmp slt i32 %additional, 0
  br i1 %neg, label %overflow, label %check
check:
  %cnt64 = sext i32 %cnt to i64
  %add64 = sext i32 %additional to i64
  %need = add i64 %cnt64, %add64
  %cap64 = sext i32 %cap to i64
  %fits = icmp sle i64 %need, %cap64
  br i1 %fits, label %done, label %grow
grow:
  %toobig = icmp sgt i64 %need, 2147483591
  br i1 %toobig, label %overflow, label %pick
pick:
  %iszero = icmp eq i32 %cap, 0
  %half = icmp sgt i64 %cap64, 1073741795
  %dbl = shl i64 %cap64, 1
  %capped = select i1 %half, i64 2147483591, i64 %dbl
  %grown = select i1 %iszero, i64 4, i64 %capped
  %useneed = icmp sgt i64 %need, %grown
  %newcap = select i1 %useneed, i64 %need, i64 %grown
  %mul = call { i64, i1 } @llvm.umul.with.overflow.i64(i64 %newcap, i64 %elem)
  %bytes = extractvalue { i64, i1 } %mul, 0
  %mulovf = extractvalue { i64, i1 } %mul, 1
  br i1 %mulovf, label %overflow, label %do_realloc
do_realloc:
  %isnone = icmp eq i64 %bytes, 0
  %alloc = select i1 %isnone, i64 1, i64 %bytes
  %old = load ptr, ptr %v
  %new = call ptr @realloc(ptr %old, i64 %alloc)
  %isnull = icmp eq ptr %new, null
  br i1 %isnull, label %oom, label %commit
commit:
  store ptr %new, ptr %v
  %newcap32 = trunc i64 %newcap to i32
  store i32 %newcap32, ptr %cap.p
  br label %done
done:
  ret void
overflow:
  call void @__vib_panic(ptr @.rt.capov, i64 21)
  unreachable
oom:
  call void @__vib_panic(ptr @.rt.oom, i64 13)
  unreachable
}

define internal void @__vib_vec_extend(ptr noalias %v, ptr readonly %src, i32 %n, i64 %elem) nounwind {
entry:
  %neg = icmp slt i32 %n, 0
  br i1 %neg, label %overflow, label %check_zero
check_zero:
  %zero = icmp eq i32 %n, 0
  br i1 %zero, label %done, label %go
go:
  call void @__vib_vec_reserve(ptr %v, i32 %n, i64 %elem)
  %cnt.p = getelementptr inbounds %vib.vec, ptr %v, i32 0, i32 1
  %cnt = load i32, ptr %cnt.p
  %data = load ptr, ptr %v
  %cnt64 = sext i32 %cnt to i64
  %off = mul i64 %cnt64, %elem
  %dst = getelementptr i8, ptr %data, i64 %off
  %n64 = zext i32 %n to i64
  %bytes = mul i64 %n64, %elem
  %bzero = icmp eq i64 %bytes, 0
  br i1 %bzero, label %commit, label %copy
copy:
  call void @llvm.memcpy.p0.p0.i64(ptr %dst, ptr %src, i64 %bytes, i1 false)
  br label %commit
commit:
  %newcnt = add i32 %cnt, %n
  store i32 %newcnt, ptr %cnt.p
  br label %done
done:
  ret void
overflow:
  call void @__vib_panic(ptr @.rt.capov, i64 21)
  unreachable
}

define internal void @__vib_vec_resize(ptr noalias %v, i32 %n, ptr readonly %fill, i64 %elem) nounwind {
entry:
  %cnt.p = getelementptr inbounds %vib.vec, ptr %v, i32 0, i32 1
  %cnt = load i32, ptr %cnt.p
  %neg = icmp slt i32 %n, 0
  br i1 %neg, label %bad, label %cmp
cmp:
  %shrink = icmp sle i32 %n, %cnt
  br i1 %shrink, label %setlen, label %growit
growit:
  %extra = sub i32 %n, %cnt
  call void @__vib_vec_reserve(ptr %v, i32 %extra, i64 %elem)
  %ezero = icmp eq i64 %elem, 0
  br i1 %ezero, label %setlen, label %prepfill
prepfill:
  %data = load ptr, ptr %v
  br label %fillloop
fillloop:
  %i = phi i32 [ %cnt, %prepfill ], [ %i.next, %fillbody ]
  %more = icmp slt i32 %i, %n
  br i1 %more, label %fillbody, label %setlen
fillbody:
  %i64 = sext i32 %i to i64
  %off = mul i64 %i64, %elem
  %dst = getelementptr i8, ptr %data, i64 %off
  call void @llvm.memcpy.p0.p0.i64(ptr %dst, ptr %fill, i64 %elem, i1 false)
  %i.next = add i32 %i, 1
  br label %fillloop
setlen:
  store i32 %n, ptr %cnt.p
  ret void
bad:
  call void @__vib_panic(ptr @.rt.neglen, i64 29)
  unreachable
}

define internal i64 @__vib_utf8_scan(ptr readonly %p, i32 %len, i32 %pos, i32 %mode) nounwind {
entry:
  %done0 = icmp sge i32 %pos, %len
  br i1 %done0, label %eof, label %check_mode
check_mode:
  %is_lines = icmp eq i32 %mode, 0
  br i1 %is_lines, label %loop_lines, label %loop_nul
loop_lines:
  %i0 = phi i32 [ %pos, %check_mode ], [ %i0.next, %cont_lines ]
  %at_end0 = icmp sge i32 %i0, %len
  br i1 %at_end0, label %tail_lines, label %body_lines
body_lines:
  %i0_64 = zext i32 %i0 to i64
  %bp0 = getelementptr inbounds i8, ptr %p, i64 %i0_64
  %b0 = load i8, ptr %bp0, align 1
  %is_lf = icmp eq i8 %b0, 10
  br i1 %is_lf, label %found_lf, label %check_cr
check_cr:
  %is_cr = icmp eq i8 %b0, 13
  br i1 %is_cr, label %found_cr, label %cont_lines
cont_lines:
  %i0.next = add i32 %i0, 1
  br label %loop_lines
found_lf:
  %next_lf = add i32 %i0, 1
  br label %pack
found_cr:
  %i0_p1 = add i32 %i0, 1
  %has_next = icmp slt i32 %i0_p1, %len
  br i1 %has_next, label %check_crlf, label %cr_single
check_crlf:
  %i0_p1_64 = zext i32 %i0_p1 to i64
  %bp1 = getelementptr inbounds i8, ptr %p, i64 %i0_p1_64
  %b1 = load i8, ptr %bp1, align 1
  %is_crlf = icmp eq i8 %b1, 10
  %i0_p2 = add i32 %i0, 2
  %next_crlf = select i1 %is_crlf, i32 %i0_p2, i32 %i0_p1
  br label %pack
cr_single:
  br label %pack
tail_lines:
  br label %pack
loop_nul:
  %i1 = phi i32 [ %pos, %check_mode ], [ %i1.next, %cont_nul ]
  %at_end1 = icmp sge i32 %i1, %len
  br i1 %at_end1, label %tail_nul, label %body_nul
body_nul:
  %i1_64 = zext i32 %i1 to i64
  %np = getelementptr inbounds i8, ptr %p, i64 %i1_64
  %nb = load i8, ptr %np, align 1
  %is_nul = icmp eq i8 %nb, 0
  br i1 %is_nul, label %found_nul, label %cont_nul
cont_nul:
  %i1.next = add i32 %i1, 1
  br label %loop_nul
found_nul:
  %next_nul = add i32 %i1, 1
  br label %pack
tail_nul:
  br label %pack
pack:
  %end = phi i32 [ %i0, %found_lf ], [ %i0, %check_crlf ], [ %i0, %cr_single ], [ %len, %tail_lines ], [ %i1, %found_nul ], [ %len, %tail_nul ]
  %next = phi i32 [ %next_lf, %found_lf ], [ %next_crlf, %check_crlf ], [ %i0_p1, %cr_single ], [ %len, %tail_lines ], [ %next_nul, %found_nul ], [ %len, %tail_nul ]
  %end64 = zext i32 %end to i64
  %next64 = zext i32 %next to i64
  %hi = shl i64 %next64, 32
  %ret = or i64 %end64, %hi
  ret i64 %ret
eof:
  ret i64 -1
}
