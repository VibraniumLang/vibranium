if (File.AppendAllText("/dev/stderr", "appended\n").IsErr(out _, out var e2)) WriteLine(e2.Error.KindName()); else WriteLine("ok");
