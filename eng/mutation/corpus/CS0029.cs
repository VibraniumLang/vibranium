class EvalCounter(int init)
{
    public int Calls = init;

    [Mut]
    public double YieldDouble(double v)
    {
        Calls += 1;
        return v;
    }

    [Mut]
    public float YieldFloat(float v)
    {
        Calls += 1;
        return v;
    }
}

static class NanPatternSuite
{
    const double CustomDoubleNan = 0.0 / 0.0;
    const float CustomFloatNan = 0.0f / 0.0f;

    static double DynamicZero(int seed) => seed == 999 ? 1.0 : 0.0;

    static double MakeDoubleSample(int kind)
    {
        double z = DynamicZero(kind);
        return kind switch
        {
            0 => -z,             // -0.0
            1 => z,              // +0.0
            2 => z - 1.5,        // -1.5
            3 => z + 2.5,        // +2.5
            4 => -1.0 / z,       // -Infinity
            5 => 1.0 / z,        // +Infinity
            6 => z / z,          // NaN
            _ => -(z / z),       // -NaN
        };
    }

    // 4. Exhaustive double & float switch expressions
    static int ClassifyDoubleExpr(double x) => x switch
    {
        < 0 => 1,
        >= 0 => 2,
        double.NaN => 3,
    };

    static int ClassifyFloatExpr(float x) => x switch
    {
        < 0f => 1,
        >= 0f => 2,
        float.NaN => 3,
    };

    static int NonExhaustiveDoubleExpr(double x) => x switch
    {
        double.NaN => 10,
        0.0 => 20,
        _ => 30,
    };

    static int NonExhaustiveFloatExpr(float x) => x switch
    {
        float.NaN => 10,
        0.0f => 20,
        _ => 30,
    };

    // 5a. Exhaustive double & float switch statements
    static int ClassifyDoubleStmt(double d)
    {
        switch (d)
        {
            case < 0:
                return 1;
            case >= 0:
                return 2;
            case double.NaN:
                return 3;
        }
    }

    static int ClassifyFloatStmt(float f)
    {
        switch (f)
        {
            case < 0f:
                return 1;
            case >= 0f:
                return 2;
            case float.NaN:
                return 3;
        }
    }

    // 5b. Traditional single-value case switch statements (ISingleValueCaseClauseOperation)
    static int SingleCaseDoubleStmt(double d)
    {
        switch (d)
        {
            case double.NaN:
                return 11;
            default:
                return 22;
        }
    }

    static int MultiCaseDoubleStmt(double d)
    {
        switch (d)
        {
            case 0.0:
                return 100;
            case double.NaN:
                return 200;
            default:
                return 300;
        }
    }

    static int SingleCaseFloatStmt(float f)
    {
        switch (f)
        {
            case float.NaN:
                return 11;
            default:
                return 22;
        }
    }

    static int GuardedDoubleStmt(double d, bool allow)
    {
        switch (d)
        {
            case double.NaN when allow:
                return 77;
            default:
                return 88;
        }
    }

    // 3. Exhaustive & non-exhaustive nested union subpatterns
    static int ClassifyOptionDouble(Option<double> od) => od switch
    {
        Some<double>(< 0) => 1,
        Some<double>(>= 0) => 2,
        Some<double>(double.NaN) => 3,
        NoneType => 4,
    };

    static int MatchOptionDoubleNan(Option<double> od) => od switch
    {
        Some<double>(double.NaN) => 1,
        _ => 2,
    };

    static char MatchOptionFloatNan(Option<float> of) => of switch
    {
        Some<float>(float.NaN) => 1,
        _ => 2,
    };

    static int MatchResultDoubleNan(Result<double, int> rd) => rd switch
    {
        Ok<double>(double.NaN) => 1,
        _ => 2,
    };

    static void TestValue(int kind, ref EvalCounter counter)
    {
        double d = MakeDoubleSample(kind);
        float f = (float)d;

        WriteLine(kind);

        // 1. Direct is / is not constant patterns (+ IEEE == / != non-regression)
        WriteLine(d is double.NaN);
        WriteLine(d is not double.NaN);
        WriteLine((float)d is float.NaN);
        WriteLine(f is float.NaN);
        WriteLine(f is not float.NaN);
        WriteLine(d is CustomDoubleNan);
        WriteLine(f is CustomFloatNan);
        if (d is double.NaN)
        {
            WriteLine(101);
        }
        else
        {
            WriteLine(102);
        }
        if (d is not double.NaN)
        {
            WriteLine(103);
        }
        else
        {
            WriteLine(104);
        }
        WriteLine(d == double.NaN);
        WriteLine(d != double.NaN);

        // Single-evaluation check on side-effecting scrutinees
        int before = counter.Calls;
        bool se1 = counter.YieldDouble(d) is double.NaN;
        bool se2 = counter.YieldDouble(d) is (< 0 or double.NaN);
        bool se3 = counter.YieldFloat(f) is (< 0f or float.NaN);
        WriteLine(se1);
        WriteLine(se2);
        WriteLine(se3);
        WriteLine(counter.Calls - before);

        // 2. Combinators
        WriteLine(d is (< 0 or double.NaN));
        WriteLine(d is not (< 0 or >= 0));
        WriteLine(f is (< 0f or float.NaN));
        WriteLine(f is not (< 0f or >= 0f));

        // 3. Nested union subpatterns
        Option<double> od = Some(d);
        Option<float> of = Some(f);
        Result<double, int> rd = Ok(d);
        WriteLine(od is Some<double>(double.NaN));
        WriteLine(od is Some<double>(not double.NaN));
        WriteLine(od is Some<double>(< 0 or double.NaN));
        WriteLine(of is Some<float>(float.NaN));
        WriteLine(rd is Ok<double>(double.NaN));
        WriteLine(MatchOptionDoubleNan(od));
        WriteLine(MatchOptionFloatNan(of));
        WriteLine(MatchResultDoubleNan(rd));
        WriteLine(ClassifyOptionDouble(od));

        // 4. Exhaustive & non-exhaustive switch expressions
        WriteLine(ClassifyDoubleExpr(d));
        WriteLine(ClassifyFloatExpr(f));
        WriteLine(NonExhaustiveDoubleExpr(d));
        WriteLine(NonExhaustiveFloatExpr(f));

        // 5. Switch statements
        WriteLine(ClassifyDoubleStmt(d));
        WriteLine(ClassifyFloatStmt(f));
        WriteLine(SingleCaseDoubleStmt(d));
        WriteLine(MultiCaseDoubleStmt(d));
        WriteLine(SingleCaseFloatStmt(f));
        WriteLine(GuardedDoubleStmt(d, true));
        WriteLine(GuardedDoubleStmt(d, false));

        // Side-effecting scrutinees in switches + Err<double>(double.NaN) + OptionRef<double>
        int swBefore = counter.Calls;
        int sw1 = counter.YieldDouble(d) switch
        {
            < 0 => 1,
            >= 0 => 2,
            double.NaN => 3,
        };
        int sw2 = SingleCaseDoubleStmt(counter.YieldDouble(d));
        WriteLine(sw1);
        WriteLine(sw2);
        WriteLine(counter.Calls - swBefore);

        int swInlineBefore = counter.Calls;
        int swInline;
        switch (counter.YieldDouble(d))
        {
            case -1.5:
            case double.NaN:
                swInline = 55;
                break;
            default:
                swInline = 66;
                break;
        }
        int swImplicitFloatNan;
        switch (d)
        {
            case float.NaN:
                swImplicitFloatNan = 71;
                break;
            default:
                swImplicitFloatNan = 72;
                break;
        }
        WriteLine(swInline);
        WriteLine(counter.Calls - swInlineBefore);
        WriteLine(swImplicitFloatNan);
        WriteLine(d is float.NaN);

        Result<int, double> rdErr = Err(d);
        WriteLine(rdErr is Err<double>(double.NaN));
        WriteLine(od.AsRef() is Some<double>(double.NaN));
        WriteLine(od is not Some<double>(double.NaN));
        WriteLine(od is (Some<double>(double.NaN) or NoneType));
        WriteLine(od is Some<double> { Value: double.NaN });
    }

    static int Main()
    {
        var counter = new EvalCounter(0);
        for (int k = 0; k < 8; k++)
        {
            TestValue(k, ref counter);
        }

        Option<double> emptyOpt = None;
        WriteLine(emptyOpt is Some<double>(double.NaN));
        WriteLine(emptyOpt is not Some<double>(double.NaN));
        WriteLine(emptyOpt is (Some<double>(double.NaN) or NoneType));
        WriteLine(MatchOptionDoubleNan(emptyOpt));
        WriteLine(ClassifyOptionDouble(emptyOpt));
        WriteLine(counter.Calls);
        return 0;
    }
}