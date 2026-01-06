using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[InProcess]
[ShortRunJob]
public class NexusBenchmarks
{
    // --- Shared sample type ---
    public class Sample
    {
        public int A { get; set; }
        public string B { get; set; }
        public long C { get; set; }
        public decimal D { get; set; }
        public DateTime E { get; set; }
        public string F { get; set; }
    }

    private static readonly PropertyInfo[] _props = typeof(Sample).GetProperties(BindingFlags.Public | BindingFlags.Instance);
    private static readonly FrozenDictionary<string, PropertyInfo> _frozen;
    private static readonly Func<Sample, Dictionary<string, object>> _compiledGetter;

    static NexusBenchmarks()
    {
        // populate a frozen dictionary for O(1) lookups
        var entries = new KeyValuePair<string, PropertyInfo>[_props.Length];
        for (int i = 0; i < _props.Length; i++) entries[i] = new KeyValuePair<string, PropertyInfo>(_props[i].Name, _props[i]);
        _frozen = entries.ToFrozenDictionary();

        // build expression-based getter (preallocate dictionary capacity)
        var param = Expression.Parameter(typeof(Sample), "s");
        var dictVar = Expression.Variable(typeof(Dictionary<string, object>), "d");
        var list = new List<Expression>();
        // use the Dictionary(int capacity) ctor to avoid resizing allocations
        var dictCtor = Expression.New(typeof(Dictionary<string, object>).GetConstructor(new[] { typeof(int) })!, Expression.Constant(_props.Length));
        list.Add(Expression.Assign(dictVar, dictCtor));
        foreach (var p in _props)
        {
            var key = Expression.Constant(p.Name);
            var propAccess = Expression.Property(param, p);
            var boxed = Expression.Convert(propAccess, typeof(object));
            var addCall = Expression.Call(dictVar, typeof(Dictionary<string, object>).GetMethod("Add"), key, boxed);
            list.Add(addCall);
        }
        list.Add(dictVar);
        var body = Expression.Block(new[] { dictVar }, list);
        var lambda = Expression.Lambda<Func<Sample, Dictionary<string, object>>>(body, param);
        _compiledGetter = lambda.Compile();
    }

    private Sample _instance = new Sample { A = 1, B = "x", C = 2, D = 3m, E = DateTime.UtcNow, F = "z" };

    // --- Experiment 1: Frozen vs Dynamic ---
    [Benchmark(Baseline = true)]
    public object Dynamic_Lookup()
    {
        // simulate per-request: find property A via GetProperties scan
        foreach (var p in typeof(Sample).GetProperties())
        {
            if (p.Name == "A") return p.GetValue(_instance);
        }
        return null;
    }

    [Benchmark]
    public object Frozen_Lookup()
    {
        // O(1) lookup
        return _frozen["A"].GetValue(_instance);
    }

    // --- Experiment 2: ExpressionTree vs Reflection ---
    [Benchmark]
    public Dictionary<string, object> Reflection_Project()
    {
        var d = new Dictionary<string, object>();
        foreach (var p in _props) d[p.Name] = p.GetValue(_instance);
        return d;
    }

    [Benchmark]
    public Dictionary<string, object> Expression_Project()
    {
        return _compiledGetter(_instance);
    }

    // --- Experiment 3: Json handler (rough) ---
    [Benchmark]
    public byte[] JsonSerializer_ToUtf8Bytes()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_instance);
    }

    [Benchmark]
    public string JsonSerializer_ToString()
    {
        return JsonSerializer.Serialize(_instance);
    }

    // --- Experiment 4: Startup cost simulation ---
    [Params(10, 100, 500)]
    public int ContractCount { get; set; }

    [Benchmark]
    public void Startup_ScanSimulation()
    {
        // simulate scanning N contract types and building metadata
        for (int i = 0; i < ContractCount; i++)
        {
            // fake property discovery work
            var props = typeof(Sample).GetProperties();
            // simulate some work per property
            foreach (var p in props)
            {
                _ = p.Name.Length;
            }
        }
    }

    public static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<NexusBenchmarks>();
    }
}
