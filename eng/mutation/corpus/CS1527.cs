var g = new G();
WriteLine(g[3]);
return 0;
private class G { public int this[int i] => i * 2; }
