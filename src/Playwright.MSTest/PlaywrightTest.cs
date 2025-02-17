/*
 * MIT License
 *
 * Copyright (c) Microsoft Corporation.
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Playwright.MSTest
{
    [TestClass]
    public class PlaywrightTest
    {
        private static int _workerCount = 0;
        private static readonly ConcurrentStack<Worker> _allWorkers = new();
        private Worker? _currentWorker;
        public static string BrowserName => (Environment.GetEnvironmentVariable("BROWSER") ?? Microsoft.Playwright.BrowserType.Chromium).ToLower();
        private static readonly Task<IPlaywright> _playwrightTask = Microsoft.Playwright.Playwright.CreateAsync();


        public IPlaywright? Playwright { get; private set; }

        public IBrowserType? BrowserType { get; private set; }

        public int WorkerIndex { get => _currentWorker!.WorkerIndex; }

        [TestInitialize]
        public async Task Setup()
        {
            try
            {
                Playwright = await _playwrightTask;
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message, e.StackTrace);
            }
            Assert.IsNotNull(Playwright, "Playwright could not be instantiated.");
            BrowserType = Playwright[BrowserName];
            Assert.IsNotNull(BrowserType, $"The requested browser ({BrowserName}) could not be found - make sure your BROWSER env variable is set correctly.");

            // get worker
            if (!_allWorkers.TryPop(out _currentWorker))
            {
                _currentWorker = new();
            }
        }

        [TestCleanup]
        public async Task Teardown()
        {
            if (TestOK)
            {
                await Task.WhenAll(_currentWorker!.InstantiatedServices.Select(x => x.ResetAsync()));
                _allWorkers.Push(_currentWorker);
            }
            else
            {
                await Task.WhenAll(_currentWorker!.InstantiatedServices.Select(x => x.DisposeAsync()));
                _currentWorker.InstantiatedServices.Clear();
            }
        }

        public async Task<T> GetService<T>(Func<T>? factory = null) where T : class, IWorkerService, new()
        {
            factory ??= () => new T();
            var serviceType = typeof(T);

            var instance = _currentWorker!.InstantiatedServices.SingleOrDefault(x => serviceType.IsInstanceOfType(x));
            if (instance == null)
            {
                instance = factory();
                await instance.BuildAsync(this);
                _currentWorker.InstantiatedServices.Add(instance);
            }

            if (instance is not T)
                throw new Exception("There was a problem instantiating the service.");

            return (T)instance;
        }

        private class Worker
        {
            public int WorkerIndex { get; } = Interlocked.Increment(ref _workerCount);
            public List<IWorkerService> InstantiatedServices { get; } = new();
        }

        protected bool TestOK
        {
            get => TestContext!.CurrentTestOutcome == UnitTestOutcome.Passed
                || TestContext!.CurrentTestOutcome == UnitTestOutcome.NotRunnable;
        }

        public TestContext? TestContext { get; set; }
    }
}
