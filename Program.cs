using System.Collections.Concurrent;
using BenchmarkDotNet.Running;
using NameSequence;

BenchmarkRunner.Run<BenchmarkNameSequence>();

// var source = Enumerable.Range(0, 100_000).ToArray();
// var rangePartitioner = Partitioner.Create(0, source.Length, source.Length/ 10);
// double[] results = new double[100_000];
//
// Parallel.ForEach(rangePartitioner, (range, _) =>
// {
//     for (int i = range.Item1; i < range.Item2; i++)
//     {
//         results[i] = Math.Exp(i);
//         
//         Console.WriteLine(results[i]);
//     }
//     Thread.Sleep(1000);
// });