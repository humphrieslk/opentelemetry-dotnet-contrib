// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Threading;
// using Google.Api;
// using Google.Apis.Compute.v1.Data;
// using Google.Cloud.Monitoring.V3;
// using Google.Protobuf.WellKnownTypes;
// using GoogleCloudOperations;
// using GoogleCloudOperations.CustomMetrics;
// using Microsoft.VisualStudio.TestTools.UnitTesting;
// using Moq;
// using Nirvana.ServerInfo;
// using OpenTelemetry.Metrics.Export;
// using Environment = Nirvana.ServerInfo.Environment;
// using Metric = OpenTelemetry.Metrics.Export.Metric;
//
// namespace Tests.GoogleCloudOperations.CustomMetrics
// {
//     [TestClass]
//     public class GoogleCloudMetricsExporterTests : TestBase
//     {
//         private static readonly DateTime EndTimestamp = DateTime.UtcNow;
//
//         [TestMethod, TestCategory("UnitTest")]
//         public void ExportAsync_DoubleSum()
//         {
//             var labelDictionary = new Dictionary<string, string>();
//             labelDictionary.Add("<test-key-1>", "<test-value-1>");
//             labelDictionary.Add("<test-key-2>", "<test-value-2>");
//
//             TestExportAsync(
//                 // open telemetry metric data
//                 AggregationType.DoubleSum,
//                 new DoubleSumData()
//                 {
//                     Sum = 5,
//                     Labels = labelDictionary.ToList(),
//                     Timestamp = EndTimestamp
//                 },
//                 // expected GCP metric data
//                 MetricDescriptor.Types.MetricKind.Cumulative,
//                 new TypedValue
//                 {
//                     DoubleValue = 5
//                 },
//                 labelDictionary);
//         }
//
//         [TestMethod, TestCategory("UnitTest")]
//         public void ExportAsync_DoubleSummary()
//         {
//             TestExportAsync(
//                 // open telemetry metric data
//                 AggregationType.DoubleSummary,
//                 new DoubleSummaryData()
//                 {
//                     Count = 2,
//                     Sum = 4.5,
//                     Labels = new List<KeyValuePair<string, string>>(),
//                     Timestamp = EndTimestamp
//                 },
//                 // expected GCP metric data
//                 MetricDescriptor.Types.MetricKind.Gauge,
//                 new TypedValue
//                 {
//                     DoubleValue = 2.25
//                 });
//         }
//
//         [TestMethod, TestCategory("UnitTest")]
//         public void ExportAsync_Int64Summary()
//         {
//             TestExportAsync(
//                 // open telemetry metric data
//                 AggregationType.Int64Summary,
//                 new Int64SummaryData()
//                 {
//                     Count = 2,
//                     Sum = 5,
//                     Labels = new List<KeyValuePair<string, string>>(),
//                     Timestamp = EndTimestamp
//                 },
//                 // expected GCP metric data
//                 MetricDescriptor.Types.MetricKind.Gauge,
//                 new TypedValue
//                 {
//                     DoubleValue = 2.5
//                 });
//         }
//
//         [TestMethod, TestCategory("UnitTest")]
//         public void ExportAsync_LongSum()
//         {
//             TestExportAsync(
//                 // open telemetry metric data
//                 AggregationType.LongSum,
//                 new Int64SumData
//                 {
//                     Sum = 15,
//                     Labels = new List<KeyValuePair<string, string>>(),
//                     Timestamp = EndTimestamp
//                 },
//                 // expected GCP metric data
//                 MetricDescriptor.Types.MetricKind.Cumulative,
//                 new TypedValue
//                 {
//                     Int64Value = 15
//                 });
//         }
//
//         [TestMethod, TestCategory("UnitTest")]
//         public void ExportAsync_DoubleSumNoData()
//         {
//             TestExportAsyncNoMetrics(
//                 AggregationType.DoubleSum,
//                 new DoubleSumData());
//         }
//
//         [TestMethod, TestCategory("UnitTest")]
//         public void ExportAsync_DoubleSummaryNoData()
//         {
//             TestExportAsyncNoMetrics(
//                 AggregationType.DoubleSummary,
//                 new DoubleSummaryData());
//         }
//
//         [TestMethod, TestCategory("UnitTest")]
//         public void ExportAsync_Int64SummaryNoData()
//         {
//             TestExportAsyncNoMetrics(
//                 AggregationType.Int64Summary,
//                 new Int64SummaryData());
//         }
//
//         [TestMethod, TestCategory("UnitTest")]
//         public void ExportAsync_LongSumNoData()
//         {
//             TestExportAsyncNoMetrics(
//                 AggregationType.LongSum,
//                 new Int64SumData());
//         }
//
//         private static void TestExportAsync(
//             AggregationType aggregationType,
//             MetricData metricData,
//             MetricDescriptor.Types.MetricKind expectedMetricKind,
//             TypedValue expectedTypedValue,
//             IDictionary<string, string> expectedLabels = null)
//         {
//             const string metricName = "<metric-name>";
//             var metric = new Metric("<metric-namespace>", metricName, "<metric-description>", aggregationType);
//             metric.Data.Add(metricData);
//             var mockCloudMonitoring = new Mock<ICloudMonitoring>();
//             var regionInfo = new RegionInfo(
//                 Environment.Dev,
//                 Cloud.Gcp, null,
//                 GcpRegion.UsEast,
//                 new Instance()
//                 {
//                     Id = ulong.MaxValue,
//                     Zone = "<instance-zone>"
//                 });
//             var expectedMonitoredResource = new MonitoredResource
//             {
//                 Type = "gce_instance"
//             };
//             expectedMonitoredResource.Labels.Add("instance_id", ulong.MaxValue.ToString());
//             expectedMonitoredResource.Labels.Add("project_id", "appsheet-development-ops");
//             expectedMonitoredResource.Labels.Add("zone", "<instance-zone>");
//             var googleCloudMetricsExporter = new GoogleCloudMetricsExporter(
//                 new Lazy<ICloudMonitoring>(() => mockCloudMonitoring.Object), regionInfo);
//             var expectedGoogleMetric = new Google.Api.Metric
//             {
//                 Type = GoogleCloudMetricsExporter.CustomMetricBaseUrl + metricName,
//             };
//             if (expectedLabels != null)
//             {
//                 expectedGoogleMetric.Labels.Add(expectedLabels);
//             }
//             var expectedTimeSeries = new TimeSeries
//             {
//                 Metric = expectedGoogleMetric,
//                 Resource = expectedMonitoredResource,
//                 MetricKind = expectedMetricKind
//             };
//             expectedTimeSeries.Points.Add(
//                 new Point
//                 {
//                     Value = expectedTypedValue,
//                     Interval = new TimeInterval
//                     {
//                         StartTime = expectedMetricKind == MetricDescriptor.Types.MetricKind.Cumulative
//                             ? Timestamp.FromDateTimeOffset(EndTimestamp.Subtract(TimeSpan.FromSeconds(15)))
//                             : null,
//                         EndTime = Timestamp.FromDateTimeOffset(EndTimestamp)
//                     }
//                 });
//             mockCloudMonitoring.Setup(
//                 cm => cm.UploadToGoogleCloudMonitoring(expectedTimeSeries)).Verifiable();
//
//             googleCloudMetricsExporter.ExportAsync(new List<Metric> {metric}, CancellationToken.None);
//
//             mockCloudMonitoring.Verify();
//         }
//
//         private static void TestExportAsyncNoMetrics(
//             AggregationType aggregationType,
//             MetricData metricData)
//         {
//             const string metricName = "<metric-name>";
//             var metric = new Metric("<metric-namespace>", metricName, "<metric-description>", aggregationType);
//             metric.Data.Add(metricData);
//             var mockCloudMonitoring = new Mock<ICloudMonitoring>();
//             var googleCloudMetricsExporter = new GoogleCloudMetricsExporter(
//                 new Lazy<ICloudMonitoring>(() => mockCloudMonitoring.Object), new RegionInfo());
//             mockCloudMonitoring
//                 .Setup(cm => cm.UploadToGoogleCloudMonitoring(It.IsAny<TimeSeries>()))
//                 .Throws(new Exception("UploadToGoogleCloudMonitoring should not be called."));
//
//             googleCloudMetricsExporter.ExportAsync(new List<Metric> {metric}, CancellationToken.None);
//
//             mockCloudMonitoring.Verify();
//         }
//     }
// }
