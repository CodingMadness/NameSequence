using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using DotNext.Reflection;

namespace NameSequence;

[Config(typeof(SomeConfigs))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class BenchmarkingReflection
{
    public static readonly TestClass[] testData = TestClass.DefineRndArray(100);
    private static DynamicInvoker? cachedInvoker;
    private static Func<TestClass, string> GetGetMethod_NET70, GetGetMethod_NET70_2;
    private static Func<TestClass, string> GetGetMethod_NEXT;
    private static MethodInfo GetNameInfo;
    private static PropertyInfo propInfo;
    private static BindingFlags bindings;
    private static Type getterDelegateType;

    [GlobalSetup]
    public void Setup()
    {
        bindings = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        propInfo = typeof(TestClass).GetProperty("Name", bindings)!;
        GetGetMethod_NET70 =
            (Func<TestClass, string>)Delegate.CreateDelegate(typeof(Func<TestClass, string>), propInfo.GetGetMethod()!);
        GetGetMethod_NEXT = typeof(TestClass).GetMethod("get_Name")!.Unreflect<Func<TestClass, string>>()!;
        GetNameInfo = typeof(TestClass).GetMethod("GetName", bindings)!;
        getterDelegateType = typeof(Func<,>).MakeGenericType(typeof(TestClass), propInfo.PropertyType);
        GetGetMethod_NET70_2 = (Func<TestClass, string>)propInfo.GetGetMethod()!.CreateDelegate(getterDelegateType);
    }

    [Benchmark]
    public string Normal_Call()
    {
        return testData[0].Name!;
    }

    [Benchmark]
    public string DotNext_Reflection()
    {
        return GetGetMethod_NEXT.Invoke(testData[0]);
    }

    [Benchmark]
    public string DotNet70_Reflection()
    {
        return GetGetMethod_NET70.Invoke(testData[0]);
    }

    [Benchmark]
    public string DotNet70_Reflection_PropertyGetValue()
    {
        return (string)propInfo.GetValue(testData[0])!;
    }

    [Benchmark]
    public string DotNet70_Reflection_PropertyBasedGetGetMethod()
    {
        return GetGetMethod_NET70_2.Invoke(testData[0]);
    }
}

[Config(typeof(SomeConfigs))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class BenchmarkNameSequence
{
    public static readonly TestClass[] testData = TestClass.DefineRndArray(174);

    [Benchmark]
    public int GetSequence_WithPinning()
    {
        using NameSequence<TestClass> sequence = new(testData, "Name", true, true);
        sequence.BuildSequence();
        sequence.Restore();
        return sequence.OnlyValues.Length;
    }

    [Benchmark]
    public string[] GetSequence_LINQ()
    {
        return testData.Select(x => x.Name).ToArray();
    }
}

public class SomeConfigs : ManualConfig
{
    public SomeConfigs()
    {
        // AddJob(Job.Default.WithGcMode(new GcMode { Force = false }));
        AddJob(Job.MediumRun.WithToolchain(InProcessNoEmitToolchain.Instance));
    }
}