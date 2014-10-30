﻿using System;
using System.Collections.Generic;
using LibGit2Sharp.Tests.TestHelpers;
using Xunit;

namespace LibGit2Sharp.Tests
{
    public class FilterFixture : BaseFixture
    {
        private readonly List<Filter> filtersForCleanUp;

        public FilterFixture()
        {
            filtersForCleanUp = new List<Filter>();
        }

        [Fact]
        public void CanNotRegisterFilterWithTheSameNameMoreThanOnce()
        {
            var filterOne = CreateFilterForAutomaticCleanUp("filter-one", "test", 1);
            var filterTwo = CreateFilterForAutomaticCleanUp("filter-one", "test", 1);

            filterOne.Register();
            Assert.Throws<InvalidOperationException>(() => filterTwo.Register());
        }

        [Fact]
        public void CanRegisterAndUnregisterTheSameFilter()
        {
            var filterOne = new Filter("filter-two", "test", 1);
            filterOne.Register();
            filterOne.Deregister();

            var filterTwo = new Filter("filter-two", "test", 1);
            filterTwo.Register();
            filterTwo.Deregister();
        }

        private Filter CreateFilterForAutomaticCleanUp(string name, string attributes, int version)
        {
            var filter = new Filter(name, attributes, version);
            filtersForCleanUp.Add(filter);
            return filter;
        }

        public override void Dispose()
        {
            foreach (var filter in filtersForCleanUp)
            {
                try
                {
                    filter.Deregister();
                }
                catch (LibGit2SharpException)
                {
                }
            }
            base.Dispose();
        }
    }
}
