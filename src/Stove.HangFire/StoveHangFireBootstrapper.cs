﻿using Autofac;

using Stove.Bootstrapping;
using Stove.Configurations;
using Stove.Hangfire;
using Stove.Threading.BackgrodunWorkers;

namespace Stove
{
    [DependsOn(
        typeof(StoveKernelBootstrapper)
    )]
    public class StoveHangFireBootstrapper : StoveBootstrapper
    {
        private readonly IBackgroundWorkerManager _backgroundWorkerManager;

        public StoveHangFireBootstrapper(IBackgroundWorkerManager backgroundWorkerManager)
        {
            _backgroundWorkerManager = backgroundWorkerManager;
        }

        public override void Start()
        {
            StoveConfiguration.Modules.StoveHangfire().Configure(configuration =>
            {
                configuration.GlobalConfiguration.UseAutofacActivator(Resolver.Resolve<ILifetimeScope>());
                StoveConfiguration.GetConfigurerIfExists<IStoveHangfireConfiguration>().Invoke(configuration);
            });

            _backgroundWorkerManager.Add(StoveConfiguration.Resolver.Resolve<HangfireBackgroundJobManager>());
            _backgroundWorkerManager.Add(StoveConfiguration.Resolver.Resolve<HangfireScheduleJobManager>());
        }
    }
}
