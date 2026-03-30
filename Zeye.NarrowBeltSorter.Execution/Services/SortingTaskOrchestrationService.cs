using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;

namespace Zeye.NarrowBeltSorter.Execution.Services {

    public class SortingTaskOrchestrationService : BackgroundService {

        protected override Task ExecuteAsync(CancellationToken stoppingToken) {
            throw new NotImplementedException();
        }
    }
}
