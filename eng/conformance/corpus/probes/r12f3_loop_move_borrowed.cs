WriteLine(Snd3(2));
static int Sink2(Vec<int> x) => x.Count;
static int Snd3(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; ReadOnlySpan<int> s = v.AsReadOnlySpan(); for (int i = 0; i < 2; i++) { acc += s[0]; acc += Sink2(v); } return acc; }
