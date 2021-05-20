﻿using Backtrace.Unity.Json;
using Backtrace.Unity.Model.JsonData;
using Backtrace.Unity.Services;
using Backtrace.Unity.Model.Metrics.Mocks;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

namespace Backtrace.Unity.Model.Metrics
{
    public class BacktraceMetricsTests
    {
        const string MetricsEventName = "scene-changed";
        // existing attribute name in Backtrace
        const string UniqueAttributeName = "scene.name";

        private AttributeProvider _attributeProvider = new AttributeProvider();

        private const string _defaultSubmissionUrl = BacktraceMetrics.DefaultSubmissionUrl;
        private const string _token = "aaaaabbbbbccccf82668682e69f59b38e0a853bed941e08e85f4bf5eb2c5458";
        private const string _universeName = "testing-universe-name";
        private readonly string _uniqueEventsSubmissionUrl = $"unique-events/submit?token={_token}&universe={_universeName}";
        private readonly string _summedEventsSubmissionUrl = $"summed-events/submit?token={_token}&universe={_universeName}";

        [OneTimeSetUp]
        public void Setup()
        {
            Debug.unityLogger.logEnabled = false;
        }
        [Test]
        public void BacktraceMetrics_ShouldTriggerUploadProcessOnSendMethodWithOnlySummedEvent_DataWasSendToTheService()
        {
            var jsonString = string.Empty;
            var submissionUrl = string.Empty;
            var requestHandler = new BacktraceHttpClientMock()
            {
                OnInvoke = (string url, BacktraceJObject json) =>
                {
                    jsonString = json.ToJson();
                    submissionUrl = url;
                }
            };
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, 0, _token, _universeName);
            backtraceMetrics.SubmissionUrl = _defaultSubmissionUrl;

            backtraceMetrics.AddSummedEvent(MetricsEventName);
            backtraceMetrics.Send();

            Assert.IsNotEmpty(jsonString);
            Assert.AreEqual(_summedEventsSubmissionUrl, submissionUrl);
            Assert.IsEmpty(backtraceMetrics.SummedEvents);
        }

        [Test]
        public void BacktraceMetrics_ShouldTriggerUploadProcessOnSendMethodWithOnlyUniqueEvent_DataWasSendToTheService()
        {
            var jsonString = string.Empty;
            var submissionUrl = string.Empty;
            var requestHandler = new BacktraceHttpClientMock()
            {
                OnInvoke = (string url, BacktraceJObject json) =>
                {
                    jsonString = json.ToJson();
                    submissionUrl = url;
                }
            };
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, 0, _token, _universeName);

            backtraceMetrics.AddUniqueEvent(UniqueAttributeName);
            backtraceMetrics.Send();

            Assert.IsNotEmpty(jsonString);
            Assert.AreEqual(submissionUrl, _uniqueEventsSubmissionUrl);
            Assert.IsEmpty(backtraceMetrics.SummedEvents);
        }


        [Test]
        public void BacktraceMetrics_ShouldntTriggerUploadWhenDataIsNotAvailable_DataWasntSendToBacktrace()
        {
            var backtraceMetrics = new BacktraceMetrics(_attributeProvider, BacktraceMetrics.DefaultSubmissionUrl, 0, _token, _universeName);
            var requestHandler = new BacktraceHttpClientMock();
            backtraceMetrics.RequestHandler = requestHandler;

            backtraceMetrics.Send();

            Assert.IsFalse(requestHandler.Called);
        }

        [UnityTest]
        public IEnumerator BacktraceMetrics_ShouldTry3TimesOn503BeforeDroppingEvents_DataWasntSendToBacktrace()
        {
            const int expectedNumberOfEventsAfterFailure = 2;
            var requestHandler = new BacktraceHttpClientMock()
            {
                StatusCode = 503
            };
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, 0, _token, _universeName);

            backtraceMetrics.AddSummedEvent(MetricsEventName);
            backtraceMetrics.AddUniqueEvent(UniqueAttributeName);
            backtraceMetrics.Send();
            for (int i = 0; i < BacktraceMetrics.MaxNumberOfAttempts; i++)
            {
                yield return new WaitForSeconds(1);
                // immidiately run next update
                var time = BacktraceMetrics.MaxTimeBetweenRequests + (BacktraceMetrics.MaxTimeBetweenRequests * i) + i + 1;
                backtraceMetrics.Tick(time);
            }

            yield return new WaitForSeconds(1);
            Assert.AreEqual(BacktraceMetrics.MaxNumberOfAttempts * 2, requestHandler.NumberOfRequests);
            Assert.AreEqual(backtraceMetrics.Count(), expectedNumberOfEventsAfterFailure);
        }


        [UnityTest]
        public IEnumerator BacktraceMetrics_ShouldTry3TimesOn503AndDropSummedEventsOnMaximumNumberOfEvents_DataWasDeleted()
        {
            const int expectedNumberOfEventsAfterFailure = 2; // unique events and we have enough place for session event so also session event
            var requestHandler = new BacktraceHttpClientMock()
            {
                StatusCode = 503
            };
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, 0, _token, _universeName);

            backtraceMetrics.AddSummedEvent(MetricsEventName);
            backtraceMetrics.AddUniqueEvent(UniqueAttributeName);
            backtraceMetrics.MaximumEvents = expectedNumberOfEventsAfterFailure;

            backtraceMetrics.Send();
            for (int i = 0; i < BacktraceMetrics.MaxNumberOfAttempts; i++)
            {
                yield return new WaitForSeconds(1);
                // immidiately run next update
                var time = BacktraceMetrics.MaxTimeBetweenRequests + (BacktraceMetrics.MaxTimeBetweenRequests * i) + i + 1;
                backtraceMetrics.Tick(time);
            }

            yield return new WaitForSeconds(1);
            Assert.AreEqual(expectedNumberOfEventsAfterFailure, backtraceMetrics.Count());
            Assert.AreEqual(BacktraceMetrics.MaxNumberOfAttempts * 2, requestHandler.NumberOfRequests);
        }

        [Test]
        public void BacktraceMetrics_ShouldTryOnlyOnceOnHttpFailure_DataWasntSendToBacktrace()
        {
            var requestHandler = new BacktraceHttpClientMock()
            {
                IsHttpError = true
            };
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, 0, _token, _universeName);

            backtraceMetrics.AddSummedEvent(MetricsEventName);
            backtraceMetrics.AddUniqueEvent(UniqueAttributeName);
            backtraceMetrics.Send();

            Assert.AreEqual(2, requestHandler.NumberOfRequests);
        }


        [Test]
        public void BacktraceMetrics_ShouldSkipMoreUniqueEventsWhenHitTheLimit_DataWasntSendToBacktrace()
        {
            const int maximumNumberOfEvents = 3;
            const int numberOfTestEvents = 10;
            var backtraceMetrics = new BacktraceMetrics(_attributeProvider, BacktraceMetrics.DefaultSubmissionUrl, 0, _token, _universeName)
            {
                MaximumEvents = maximumNumberOfEvents
            };

            for (int i = 0; i < numberOfTestEvents; i++)
            {
                backtraceMetrics.AddUniqueEvent($"{UniqueAttributeName} {i}", new Dictionary<string, string>() { { $"{UniqueAttributeName} {i}", "value" } });
            }

            Assert.AreEqual(maximumNumberOfEvents, backtraceMetrics.Count());
        }

        [Test]
        public void BacktraceMetrics_ShouldSkipMoreSummedEventsWhenHitTheLimit_DataWasntSendToBacktrace()
        {
            const int maximumNumberOfEvents = 3;
            const int numberOfTestEvents = 10;
            var backtraceMetrics = new BacktraceMetrics(_attributeProvider, BacktraceMetrics.DefaultSubmissionUrl, 0, _token, _universeName)
            {
                MaximumEvents = maximumNumberOfEvents
            };

            for (int i = 0; i < numberOfTestEvents; i++)
            {
                backtraceMetrics.AddSummedEvent($"{MetricsEventName} {i}");
            }

            Assert.AreEqual(maximumNumberOfEvents, backtraceMetrics.Count());
        }

        [Test]
        public void BacktraceMetrics_ShouldTriggerUploadViaTickMethodWhenReachedMaximumNumberOfEvents_DataWasSendToBacktrace()
        {
            const int maximumNumberOfEvents = 3;
            const int defaultTimeIntervalInSec = 10;
            var requestHandler = new BacktraceHttpClientMock();
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, defaultTimeIntervalInSec, _token, _universeName)
            {
                MaximumEvents = maximumNumberOfEvents
            };

            for (int i = 0; i < maximumNumberOfEvents; i++)
            {
                backtraceMetrics.AddSummedEvent($"{MetricsEventName} {i}");
            }

            backtraceMetrics.Tick(defaultTimeIntervalInSec + 1);

            Assert.AreEqual(0, backtraceMetrics.Count());
            Assert.AreEqual(1, requestHandler.NumberOfRequests);
        }

        [Test]
        public void BacktraceMetrics_ShouldntTriggerDownloadViaTickMethodWhenDidntReachMaximumNumberOfEvents_DataWasSendToBacktrace()
        {
            const int maximumNumberOfEvents = 3;
            const int expectedNumberOfEvents = 2;
            var backtraceMetrics = new BacktraceMetrics(_attributeProvider, BacktraceMetrics.DefaultSubmissionUrl, 0, _token, _universeName)
            {
                MaximumEvents = maximumNumberOfEvents
            };
            var requestHandler = new BacktraceHttpClientMock();
            backtraceMetrics.RequestHandler = requestHandler;
            for (int i = 0; i < expectedNumberOfEvents; i++)
            {
                backtraceMetrics.AddSummedEvent($"{MetricsEventName} {i}");
            }

            backtraceMetrics.Tick(0);

            Assert.AreEqual(expectedNumberOfEvents, backtraceMetrics.Count());
            Assert.AreEqual(0, requestHandler.NumberOfRequests);
        }


        [Test]
        public void BacktraceMetrics_ShouldTriggerUploadAfterTimeIntervalHit_DataWasSendToBacktrace()
        {
            const int timeInterval = 10;
            const int expectedNumberOfEvents = 1; // we send successfully session event so we have one unique event 
            const int expectedNumberOfRequests = 2;
            var requestHandler = new BacktraceHttpClientMock();
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, timeInterval, _token, _universeName);

            backtraceMetrics.AddSummedEvent(MetricsEventName);
            backtraceMetrics.AddUniqueEvent(UniqueAttributeName);
            backtraceMetrics.Tick(timeInterval + 1);


            Assert.AreEqual(expectedNumberOfEvents, backtraceMetrics.Count());
            Assert.AreEqual(expectedNumberOfRequests, requestHandler.NumberOfRequests);
        }

        [Test]
        public void BacktraceMetrics_ShouldntTriggerDownloadBeforeTimeIntervalHit_DataWasSendToBacktrace()
        {
            const int timeInterval = 10;
            const int numberOfAddedEvents = 2;
            var requestHandler = new BacktraceHttpClientMock();
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, timeInterval, _token, _universeName);

            backtraceMetrics.AddSummedEvent(MetricsEventName);
            backtraceMetrics.AddUniqueEvent(UniqueAttributeName);

            backtraceMetrics.Tick(timeInterval - 1);

            Assert.AreEqual(numberOfAddedEvents, backtraceMetrics.Count());
            Assert.AreEqual(0, requestHandler.NumberOfRequests);
        }


        [Test]
        public void BacktraceMetrics_ShouldTriggerUploadAfterTimeIntervalHitAgain_DataWasSendToBacktrace()
        {
            const int timeInterval = 10;
            var requestHandler = new BacktraceHttpClientMock();
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, timeInterval, _token, _universeName);

            backtraceMetrics.AddSummedEvent(MetricsEventName);
            backtraceMetrics.AddUniqueEvent(UniqueAttributeName);

            backtraceMetrics.Tick(timeInterval + 1);

            backtraceMetrics.AddSummedEvent(MetricsEventName);
            backtraceMetrics.AddUniqueEvent(UniqueAttributeName);

            backtraceMetrics.Tick((timeInterval * 2) + 1);

            // we added two unique events - but because we added two the same reports
            // they should be combined
            Assert.AreEqual(1, backtraceMetrics.Count());
            Assert.AreEqual(4, requestHandler.NumberOfRequests);
        }

        [Test]
        public void BacktraceMetrics_ShouldntTriggerDownloadAfterTimeIntervalFirstHit_DataWasSendToBacktrace()
        {
            const int timeInterval = 10;
            var requestHandler = new BacktraceHttpClientMock();
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, timeInterval, _token, _universeName);

            backtraceMetrics.AddSummedEvent(MetricsEventName);
            backtraceMetrics.AddUniqueEvent(UniqueAttributeName);

            backtraceMetrics.Tick(timeInterval + 1);

            backtraceMetrics.AddSummedEvent(MetricsEventName);
            backtraceMetrics.AddUniqueEvent(UniqueAttributeName);

            backtraceMetrics.Tick(timeInterval + 2);

            Assert.AreEqual(2, requestHandler.NumberOfRequests);
            Assert.AreEqual(2, backtraceMetrics.Count());
        }

        [Test]
        public void BacktraceMetricsDefaultEvent_ShouldSendDefaultEventOnTheApplicationStartup_DataWasSendToBacktrace()
        {
            var requestHandler = new BacktraceHttpClientMock();
            var backtraceMetrics = new BacktraceMetrics(requestHandler, _attributeProvider, 10, _token, _universeName);

            backtraceMetrics.SendStartupEvent();

            Assert.AreEqual(2, requestHandler.NumberOfRequests);
        }

    }
}
