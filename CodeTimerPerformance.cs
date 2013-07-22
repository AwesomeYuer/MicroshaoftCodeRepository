//#define NET35
namespace TestConsoleApplication
{
	using System;
	using System.Diagnostics;
	using System.Threading;
	using Microshaoft;
	class Program
	{
		static void Main(string[] args)
		{
			Console.WriteLine("Begin ...");
			Random r = new Random();
			int sleep = 10;
			int iterations = 2000;
			int maxDegreeOfParallelism = 8; // Environment.ProcessorCount;
			var performanceCountersCategoryName = "Microshaoft EasyPerformanceCounters Category";
			var performanceCountersCategoryInstanceName = string.Format
																	(
																		"{2}{0}{3}{1}{4}"
																		, ": "
																		, " @ "
																		, ""
																		, ""
																		, Process.GetCurrentProcess().ProcessName
																	);
			//EasyPerformanceCountersHelper 调用示例
			//调用 EasyPerformanceCountersHelper.AttachPerformanceCountersCategoryInstance 可加载性能计数器
			EasyPerformanceCountersHelper.AttachPerformanceCountersCategoryInstance
								(
									performanceCountersCategoryName
									, performanceCountersCategoryInstanceName + "-1"
								);
			var enableCounters = MultiPerformanceCountersTypeFlags.ProcessCounter
									| MultiPerformanceCountersTypeFlags.ProcessedAverageTimerCounter
									| MultiPerformanceCountersTypeFlags.ProcessedCounter
									| MultiPerformanceCountersTypeFlags.ProcessedRateOfCountsPerSecondCounter
									| MultiPerformanceCountersTypeFlags.ProcessingCounter;
			//EasyPerformanceCountersHelper 可以直接使用 比如 用于 ASP.NET page_load 程序中代码中
			EasyPerformanceCountersHelper.CountPerformance
								(
									enableCounters
									, performanceCountersCategoryName
									, performanceCountersCategoryInstanceName + "-1"
									, null
									, () =>
									{
										//需要性能计数器的代码段
										//begin ==============================================
										var x = r.Next(0, 10) * sleep;
										Thread.Sleep(x);
										//end ================================================
									}
									, null
								);
			//CodeTimerPerformance 调用示例
			//CodeTimerPerformance.AttachPerformanceCountersCategoryInstance 可加载性能计数器
			CodeTimerPerformance.AttachPerformanceCountersCategoryInstance
								(
									performanceCountersCategoryName
									, performanceCountersCategoryInstanceName + "-2"
								);
			enableCounters =
								MultiPerformanceCountersTypeFlags.ProcessCounter
								| MultiPerformanceCountersTypeFlags.ProcessingCounter
								| MultiPerformanceCountersTypeFlags.ProcessedCounter
								| MultiPerformanceCountersTypeFlags.ProcessedAverageTimerCounter
								| MultiPerformanceCountersTypeFlags.ProcessedRateOfCountsPerSecondCounter;
			//enableCounters = MultiPerformanceCountersTypeFlags.None;
			CodeTimerPerformance.ParallelTime
						(
							"ParallelTime1"
							, iterations
							, () =>
							{
								//需要性能计数器的代码段
								//begin ==============================================
								var x = r.Next(0, 10) * sleep;
								Thread.Sleep(x);
								//end ================================================
							}
							, maxDegreeOfParallelism
							, enableCounters
							, performanceCountersCategoryName
							, performanceCountersCategoryInstanceName + "-1"
						);
			CodeTimerPerformance.Time
						(
							"Time2"
							, iterations
							, () =>
							{
								//需要性能计数器的代码段
								//begin ==============================================
								var x = r.Next(0, 10) * sleep;
								Thread.Sleep(x);
								//end ================================================
							}
							//, maxDegreeOfParallelism
							, enableCounters
							, performanceCountersCategoryName
							, performanceCountersCategoryInstanceName + "-2"
						);
			Console.WriteLine("End ...");
			Console.ReadLine();
		}
	}
}
namespace Microshaoft
{
	using System;
	using System.Diagnostics;
	using System.Runtime.InteropServices;
	using System.Threading;
	using System.Threading.Tasks;
	public static class CodeTimerPerformance
	{
		public static void Initialize()
		{
			Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
			Thread.CurrentThread.Priority = ThreadPriority.Highest;
			Time("", 1, () => { }, MultiPerformanceCountersTypeFlags.None, string.Empty, string.Empty);
		}
		public static void AttachPerformanceCountersCategoryInstance
					(
						string performanceCountersCategoryName
						, string performanceCountersCategoryInstanceName
					)
		{
			EasyPerformanceCountersHelper.AttachPerformanceCountersCategoryInstance
					(
						performanceCountersCategoryName
						, performanceCountersCategoryInstanceName
					);
		}
		public static void ParallelTime
								(
									string name
									, int iterations
									, Action actionOnce
									, int maxDegreeOfParallelism //= 1
									, MultiPerformanceCountersTypeFlags enablePerformanceCounters //= false
									, string performanceCountersCategoryName
									, string performanceCountersCategoryInstanceName
								)
		{
			// 1.
			ConsoleColor currentForeColor = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine(name);
			// 2.
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
			int[] gcCounts = new int[GC.MaxGeneration + 1];
			for (int i = 0; i <= GC.MaxGeneration; i++)
			{
				gcCounts[i] = GC.CollectionCount(i);
			}
			IntPtr threadID = GetCurrentThreadId();
			Stopwatch watch = Stopwatch.StartNew();
			ulong cycleCount = GetCurrentThreadCycleCount();
			

			Parallel.For
						(
							0
							, iterations
							, new ParallelOptions()
							{
								MaxDegreeOfParallelism = maxDegreeOfParallelism
								//, TaskScheduler = null
							}
							, (x) =>
							{
								EasyPerformanceCountersHelper.CountPerformance
												(
													enablePerformanceCounters
													, performanceCountersCategoryName
													, performanceCountersCategoryInstanceName
													, null
													, actionOnce
													, null
												);
							}
						);
			ulong cpuCycles = GetCurrentThreadCycleCount() - cycleCount;
			watch.Stop();
			//watch = null;
			// 4.
			Console.ForegroundColor = currentForeColor;
			Console.WriteLine
							(
								"{0}Time Elapsed:{0}{1}ms"
								, "\t"
								, watch.ElapsedMilliseconds.ToString("N0")
							);
			Console.WriteLine
							(
								"{0}CPU Cycles:{0}{1}"
								, "\t"
								, cpuCycles.ToString("N0")
							);
			// 5.
			for (int i = 0; i <= GC.MaxGeneration; i++)
			{
				int count = GC.CollectionCount(i) - gcCounts[i];
				Console.WriteLine
							(
								"{0}Gen{1}:{0}{0}{2}"
								, "\t"
								, i
								, count
							);
			}
			Console.WriteLine();
		}
		public static void Time
							(
								string name
								, int iterations
								, Action actionOnce
								, MultiPerformanceCountersTypeFlags enablePerformanceCounters //= false
								, string performanceCountersCategoryName
								, string performanceCountersCategoryInstanceName
							)
		{
			ParallelTime
						(
							name
							, iterations
							, actionOnce
							, 1
							, enablePerformanceCounters
							, performanceCountersCategoryName
							, performanceCountersCategoryInstanceName
						);
		}
		private static ulong GetThreadCycleCount(IntPtr threadID)
		{
			ulong cycleCount = 0;
			QueryThreadCycleTime(threadID, ref cycleCount);
			return cycleCount;
		}
		private static ulong GetCurrentThreadCycleCount()
		{
			IntPtr threadID = GetCurrentThread();
			return GetThreadCycleCount(threadID);
		}
		[DllImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool QueryThreadCycleTime(IntPtr threadHandle, ref ulong cycleTime);
		[DllImport("kernel32.dll")]
		static extern IntPtr GetCurrentThread();
		[DllImport("kernel32.dll")]
		static extern IntPtr GetCurrentThreadId();
	}
}
namespace Microshaoft
{
	using System;
	using System.Collections.Generic;
	//using System.Collections.Concurrent;
	public static class EasyPerformanceCountersHelper
	{
		private static Dictionary<string, PerformanceCountersContainer> _dictionary = new Dictionary<string, PerformanceCountersContainer>();
		public static void AttachPerformanceCountersCategoryInstance
							(
								string performanceCountersCategoryName
								, string performanceCountersCategoryInstanceName
							)
		{
			string key = string.Format
									(
										"{1}{0}{2}"
										, "-"
										, performanceCountersCategoryName
										, performanceCountersCategoryInstanceName
									);
			PerformanceCountersContainer container = null;
			if (!_dictionary.TryGetValue(key, out container))
			{
				container = new PerformanceCountersContainer();
				_dictionary.Add
							(
								key
								, container //new PerformanceCountersContainer()
							);
				container.AttachPerformanceCountersToProperties(performanceCountersCategoryInstanceName, performanceCountersCategoryName);
			}
		}
		private static object _lockerObject = new object();
		public static void CountPerformance
									(
										MultiPerformanceCountersTypeFlags enabledPerformanceCounters
										, string performanceCountersCategoryName
										, string performanceCountersCategoryInstanceName
										, Action beforeCountPerformanceInnerProcessAction
										, Action countPerformanceInnerProcessAction
										, Action afterCountPerformanceInnerProcessAction
									)
		{
			if (enabledPerformanceCounters != MultiPerformanceCountersTypeFlags.None)
			{
				if (countPerformanceInnerProcessAction != null)
				{
					string key = string.Format
											(
												"{1}{0}{2}"
												, "-"
												, performanceCountersCategoryName
												, performanceCountersCategoryInstanceName
											);
					PerformanceCountersContainer container = null;
					if (!_dictionary.TryGetValue(key, out container))
					{
						lock (_lockerObject)
						{
							container = new PerformanceCountersContainer();
							_dictionary.Add
										(
											key
											, new PerformanceCountersContainer()
										);
							container.AttachPerformanceCountersToProperties
												(
													performanceCountersCategoryInstanceName
													, performanceCountersCategoryName
												);
						}
					}
					var enableProcessCounter =
												(
													(enabledPerformanceCounters & MultiPerformanceCountersTypeFlags.ProcessCounter)
													!= MultiPerformanceCountersTypeFlags.None
												);
					if (enableProcessCounter)
					{
						container.PrcocessPerformanceCounter.Increment();
					}
					var enableProcessingCounter =
												(
													(enabledPerformanceCounters & MultiPerformanceCountersTypeFlags.ProcessingCounter)
													!= MultiPerformanceCountersTypeFlags.None
												);
					if (enableProcessingCounter)
					{
						container.ProcessingPerformanceCounter.Increment();
					}
					var enableProcessedAverageTimerCounter =
												(
													(enabledPerformanceCounters & MultiPerformanceCountersTypeFlags.ProcessedAverageTimerCounter)
													!= MultiPerformanceCountersTypeFlags.None
												);
					container.ProcessedAverageTimerPerformanceCounter.ChangeAverageTimerCounterValueWithTryCatchExceptionFinally
															(
																enableProcessedAverageTimerCounter
																, container.ProcessedAverageBasePerformanceCounter
																, () =>
																{
																	if (countPerformanceInnerProcessAction != null)
																	{
																		if (beforeCountPerformanceInnerProcessAction != null)
																		{
																			beforeCountPerformanceInnerProcessAction();
																		}
																		countPerformanceInnerProcessAction();
																		if (afterCountPerformanceInnerProcessAction != null)
																		{
																			afterCountPerformanceInnerProcessAction();
																		}
																	}
																}
																, null
																, null
															);
					if (enableProcessingCounter)
					{
						container.ProcessingPerformanceCounter.Decrement();
					}
					var enableProcessedPerformanceCounter =
											(
												(enabledPerformanceCounters & MultiPerformanceCountersTypeFlags.ProcessedCounter)
												!= MultiPerformanceCountersTypeFlags.None
											);
					if (enableProcessedPerformanceCounter)
					{
						container.ProcessedPerformanceCounter.Increment();
					}
					var enableProcessedRateOfCountsPerSecondPerformanceCounter =
											(
												(enabledPerformanceCounters & MultiPerformanceCountersTypeFlags.ProcessedRateOfCountsPerSecondCounter)
												!= MultiPerformanceCountersTypeFlags.None
											);
					if (enableProcessedRateOfCountsPerSecondPerformanceCounter)
					{
						container.ProcessedRateOfCountsPerSecondPerformanceCounter.Increment();
					}
				}
			}
			else
			{
				if (countPerformanceInnerProcessAction != null)
				{
					countPerformanceInnerProcessAction();
				}
			}
		}
	}
}
//=========================================================================================
