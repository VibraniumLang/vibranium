using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// c6 delta: witnesses for main's F12 decision. `Option<T>.AsRef()` (`this in Option<T>` since cut C10) needs a receiver
// with a storage location; a temporary receiver is VIB000 on every frontend, and a local stays valid. Add this file
// together with the SPEC §3.3 correction (SPEC-CORRECTIONS.diff) and the R3.3-asref-storage-location ledger rule. On c5
// the reject fails: the Analyzer and Stage-0 accept the temporary (only vib-self rejects it).
public sealed class Round12RuleWitnessAsRefTests
{
    [Fact]
    public Task R3_3_AsRefStorageLocation_Reject() => Round12RuleWitness.RejectAsync("""
        WriteLine(MakeNode().AsRef().IsSome);
        static Option<Node> MakeNode() => Some(new Node());
        sealed class Node { public int V = 7; }
        """, "VIB000");

    // The same call in static form: a temporary argument is VIB000 as well.
    [Fact]
    public Task R3_3_AsRefStorageLocation_Reject_StaticForm() => Round12RuleWitness.RejectAsync("""
        WriteLine(OptionExtensions.AsRef(MakeNode()).IsSome);
        static Option<Node> MakeNode() => Some(new Node());
        sealed class Node { public int V = 7; }
        """, "VIB000");

    [Fact]
    public Task R3_3_AsRefStorageLocation_Accept() => Round12RuleWitness.AcceptAsync("""
        Option<Node> o = MakeNode();
        var r = o.AsRef();
        WriteLine(r.IsSome);
        WriteLine(r switch { Some<Node>(var n) => n.V, _ => 0 });
        static Option<Node> MakeNode() => Some(new Node());
        sealed class Node { public int V = 7; }
        """, "True\n7\n");

    // The static form on a local is valid too and must stay accepted by every frontend.
    [Fact]
    public Task R3_3_AsRefStorageLocation_Accept_StaticForm() => Round12RuleWitness.AcceptAsync("""
        Option<Node> o = MakeNode();
        var r = OptionExtensions.AsRef(o);
        WriteLine(r.IsSome);
        WriteLine(r switch { Some<Node>(var n) => n.V, _ => 0 });
        static Option<Node> MakeNode() => Some(new Node());
        sealed class Node { public int V = 7; }
        """, "True\n7\n");
}
