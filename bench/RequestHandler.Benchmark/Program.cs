// See https://aka.ms/new-console-template for more information

using System;
using BenchmarkDotNet.Running;
using RequestHandler.Benchmark;

BenchmarkSwitcher.FromAssembly(typeof(Bench).Assembly);

Console.WriteLine("TEST");
