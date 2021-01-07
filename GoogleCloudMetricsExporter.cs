using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Api;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.Monitoring.V3;
using Google.Protobuf.WellKnownTypes;
using OpenTelemetry.Metrics.Export;
using Metric = OpenTelemetry.Metrics.Export.Metric;
using OpenTelemetry.Metrics;

namespace GoogleCloudOperations.CustomMetrics
{
    public class GoogleCloudMetricsExporter : MetricExporter
    {
        public static readonly string CustomMetricBaseUrl = "custom.googleapis.com/server/";

        private readonly MetricServiceClient _metricServiceClient;
        private readonly ProjectName _projectName;
        private readonly MonitoredResource _monitoredResource;

        public GoogleCloudMetricsExporter(ProjectName projectName, MonitoredResource monitoredResource)
        {;
            _metricServiceClient = MetricServiceClient.Create();
            _monitoredResource = monitoredResource;
            _projectName = projectName;
        }

        public override Task<ExportResult> ExportAsync(IEnumerable<Metric> metrics,
            CancellationToken cancellationToken)
        {
            var enumerator = metrics.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var current = enumerator.Current;

                foreach (var metricData in current.Data)
                {
                    // No count information is available except in the type specific metricData, so "no metric data"
                    // case is handled in the methods which convert the metrics and send the data to GCP
                    switch (current.AggregationType)
                    {
                        case AggregationType.DoubleSum:
                        {
                            WriteSum(
                                GenerateNewTimeSeries(
                                    current.MetricName,
                                    metricData.Labels,
                                    MetricDescriptor.Types.MetricKind.Cumulative),
                                (DoubleSumData)metricData);
                            break;
                        }

                        case AggregationType.DoubleSummary:
                        {
                            WriteSummary(
                                GenerateNewTimeSeries(
                                    current.MetricName,
                                    metricData.Labels,
                                    MetricDescriptor.Types.MetricKind.Gauge),
                                (DoubleSummaryData)metricData);
                            break;
                        }

                        case AggregationType.Int64Summary:
                        {
                            WriteSummary(
                                GenerateNewTimeSeries(
                                    current.MetricName,
                                    metricData.Labels,
                                    MetricDescriptor.Types.MetricKind.Gauge),
                                (Int64SummaryData)metricData);
                            break;
                        }

                        case AggregationType.LongSum:
                        {
                            WriteSum(
                                GenerateNewTimeSeries(
                                    current.MetricName,
                                    metricData.Labels,
                                    MetricDescriptor.Types.MetricKind.Cumulative),
                                (Int64SumData)metricData);
                            break;
                        }

                        case AggregationType.DoubleDistribution:
                        {
                            WriteDistribution(
                                GenerateNewTimeSeries(
                                    current.MetricName,
                                    metricData.Labels,
                                    MetricDescriptor.Types.MetricKind.Gauge),
                                (DoubleDistributionData)metricData);
                            break;
                        }

                        case AggregationType.Int64Distribution:
                        {
                            WriteDistribution(
                                GenerateNewTimeSeries(
                                    current.MetricName,
                                    metricData.Labels,
                                    MetricDescriptor.Types.MetricKind.Gauge),
                                (Int64DistributionData)metricData);
                            break;
                        }

                        default:
                            throw new NotSupportedException(
                                $"Unsupported aggregation type: {current.AggregationType}");
                    }
                }
            }
            enumerator.Dispose();

            return Task.FromResult(ExportResult.Success);
        }

        private TimeSeries GenerateNewTimeSeries(
            string metricName,
            IEnumerable<KeyValuePair<string, string>> labelSet,
            MetricDescriptor.Types.MetricKind metricKind)
        {
            var googleMetric = new Google.Api.Metric
            {
                Type = CustomMetricBaseUrl + metricName
            };
            if (labelSet != null)
            {
                googleMetric.Labels.Add(
                    labelSet.ToDictionary(x => x.Key, x => x.Value));
            }

            return new TimeSeries
            {
                Metric = googleMetric,
                Resource = _monitoredResource,
                MetricKind = metricKind
            };
        }

        private void WriteDistribution(TimeSeries timeSeries, Int64DistributionData int64DistributionData)
        {
            if (int64DistributionData.Count == 0)
            {
                return;
            }

            var distribution = new TypedValue
            {
                DistributionValue = new Distribution
                {
                    BucketCounts = { int64DistributionData.BucketCounts },
                    BucketOptions = GetBucketOptions(int64DistributionData.AggregationOptions),
                    Count = int64DistributionData.Count,
                    Mean = int64DistributionData.Mean,
                    SumOfSquaredDeviation = int64DistributionData.SumOfSquaredDeviation,
                }
            };

            UploadToGoogleCloudMonitoring(timeSeries, distribution, GetGaugeInterval(int64DistributionData));
        }

        private void WriteDistribution(TimeSeries timeSeries, DoubleDistributionData doubleDistributionData)
        {
            if (doubleDistributionData.Count == 0)
            {
                return;
            }

            var distribution = new TypedValue
            {
                DistributionValue = new Distribution
                {
                    BucketCounts = { doubleDistributionData.BucketCounts },
                    BucketOptions = GetBucketOptions(doubleDistributionData.AggregationOptions),
                    Count = doubleDistributionData.Count,
                    Mean = doubleDistributionData.Mean,
                    SumOfSquaredDeviation = doubleDistributionData.SumOfSquaredDeviation,
                }
            };


            UploadToGoogleCloudMonitoring(timeSeries, distribution, GetGaugeInterval(doubleDistributionData));
        }

        private Distribution.Types.BucketOptions GetBucketOptions(AggregationOptions aggregationOptions)
        {
            switch (aggregationOptions)
            {
                case DoubleExplicitDistributionOptions doubleExplicitDistributionOptions:
                    return new Distribution.Types.BucketOptions
                    {
                        ExplicitBuckets = new Distribution.Types.BucketOptions.Types.Explicit
                        {
                            Bounds = { doubleExplicitDistributionOptions.Bounds },
                        },
                    };
                case DoubleExponentialDistributionOptions doubleExponentialDistributionOptions:
                    return new Distribution.Types.BucketOptions
                    {
                        ExponentialBuckets = new Distribution.Types.BucketOptions.Types.Exponential
                        {
                            GrowthFactor = doubleExponentialDistributionOptions.GrowthFactor,
                            NumFiniteBuckets = doubleExponentialDistributionOptions.NumberOfFiniteBuckets,
                            Scale = doubleExponentialDistributionOptions.Scale,
                        }
                    };
                case DoubleLinearDistributionOptions doubleLinearDistributionOptions:
                    return new Distribution.Types.BucketOptions
                    {
                        LinearBuckets = new Distribution.Types.BucketOptions.Types.Linear
                        {
                            NumFiniteBuckets = doubleLinearDistributionOptions.NumberOfFiniteBuckets,
                            Offset = doubleLinearDistributionOptions.Offset,
                            Width = doubleLinearDistributionOptions.Width,
                        },
                    };
                case Int64ExplicitDistributionOptions int64ExplicitDistributionOptions:
                    return new Distribution.Types.BucketOptions
                    {
                        ExplicitBuckets = new Distribution.Types.BucketOptions.Types.Explicit
                        {
                            Bounds = { int64ExplicitDistributionOptions.Bounds.Select(val => (double) val).ToArray() },
                        },
                    };
                case Int64ExponentialDistributionOptions int64ExponentialDistributionOptions:
                    return new Distribution.Types.BucketOptions
                    {
                        ExponentialBuckets = new Distribution.Types.BucketOptions.Types.Exponential
                        {
                            GrowthFactor = int64ExponentialDistributionOptions.GrowthFactor,
                            NumFiniteBuckets = int64ExponentialDistributionOptions.NumberOfFiniteBuckets,
                            Scale = int64ExponentialDistributionOptions.Scale,
                        },
                    };
                case Int64LinearDistributionOptions int64LinearDistributionOptions:
                    return new Distribution.Types.BucketOptions
                    {
                        LinearBuckets = new Distribution.Types.BucketOptions.Types.Linear
                        {
                            NumFiniteBuckets = int64LinearDistributionOptions.NumberOfFiniteBuckets,
                            Offset = int64LinearDistributionOptions.Offset,
                            Width = int64LinearDistributionOptions.Width,
                        },
                    };
            }

            throw new NotSupportedException($"Provided aggregation options are not supported: {aggregationOptions}");
        }

        private void WriteSum(TimeSeries timeSeries, Int64SumData int64SumData)
        {
            if (int64SumData.Sum == 0)
            {
                return;
            }

            var value = new TypedValue
            {
                Int64Value = int64SumData.Sum
            };
            var timeInterval = GetCumulativeInterval(int64SumData);


            UploadToGoogleCloudMonitoring(timeSeries, value, timeInterval);
        }

        private void WriteSum(TimeSeries timeSeries, DoubleSumData doubleSumData)
        {
            if (doubleSumData.Sum == 0)
            {
                return;
            }

            var value = new TypedValue
            {
                DoubleValue = doubleSumData.Sum
            };
            var timeInterval = GetCumulativeInterval(doubleSumData);

            UploadToGoogleCloudMonitoring(timeSeries, value, timeInterval);
        }

        private void WriteSummary(TimeSeries timeSeries, double sum, long count, DateTime timestamp)
        {
            if (count == 0)
            {
                return;
            }

            var value = new TypedValue
            {
                DoubleValue = sum / count
            };
            var timeInterval = new TimeInterval
            {
                EndTime = Timestamp.FromDateTimeOffset(timestamp)
            };

            UploadToGoogleCloudMonitoring(timeSeries, value, timeInterval);
        }

        private void WriteSummary(TimeSeries timeSeries, Int64SummaryData int64SummaryData)
        {
            WriteSummary(timeSeries, int64SummaryData.Sum, int64SummaryData.Count, int64SummaryData.Timestamp);
        }

        private void WriteSummary(TimeSeries timeSeries, DoubleSummaryData doubleSummaryData)
        {
            WriteSummary(timeSeries, doubleSummaryData.Sum, doubleSummaryData.Count, doubleSummaryData.Timestamp);
        }

        private static TimeInterval GetGaugeInterval(MetricData metricData)
        {
            return new TimeInterval
            {
                StartTime = Timestamp.FromDateTimeOffset(metricData.Timestamp),
                EndTime = Timestamp.FromDateTimeOffset(metricData.Timestamp)
            };
        }
        private static TimeInterval GetCumulativeInterval(MetricData metricData)
        {
            return new TimeInterval
            {
                StartTime = Timestamp.FromDateTimeOffset(metricData.StartTimestamp),
                EndTime = Timestamp.FromDateTimeOffset(metricData.Timestamp)
            };
        }

        private void UploadToGoogleCloudMonitoring(TimeSeries timeSeries, TypedValue value, TimeInterval timeInterval)
        {
            timeSeries.Points.Add(
                new Point
                {
                    Value = value,
                    Interval = timeInterval
                });
            try
            {
                _metricServiceClient.CreateTimeSeries(_projectName, new[] {timeSeries});
            }
            catch(Exception e)
            {
                var exception = e;
            }
        }
    }
}
