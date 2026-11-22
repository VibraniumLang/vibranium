if (File.AppendAllText("/dev/stderr", "appended\n").IsErr(ref _, out var e2)) WriteLine(e2.Error.KindName()); else WriteLine("ok");
