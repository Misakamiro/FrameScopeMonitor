using System;
using System.Collections.Generic;
using System.IO;
public static class StopFixtureConfig {
 public static void Main(string[] args) {
  var config = FrameScopeConfigStore.CreateDefaultConfig();
  config.DataRoot = args[1];
  config.Targets = new List<FrameScopeTarget> { new FrameScopeTarget { Name = "StopFixture", ProcessName = args[2] + ".exe", Enabled = true } };
  config.OpenReportOnComplete = false;
  config.PollIntervalMs = 250;
  FrameScopeConfigStore.Save(args[0], config);
  if (!File.Exists(args[0])) throw new InvalidOperationException("Fixture config missing.");
  var loaded = FrameScopeConfigStore.Load(args[0]);
  FrameScopeConfigStore.Normalize(loaded);
  if (!string.Equals(Path.GetFullPath(loaded.DataRoot), Path.GetFullPath(args[1]), StringComparison.OrdinalIgnoreCase) ||
      loaded.Targets == null || loaded.Targets.Count != 1 || !loaded.Targets[0].Enabled ||
      loaded.Targets[0].ProcessName != args[2] + ".exe")
      throw new InvalidOperationException("Fixture config failed post-load isolation verification.");
 }
}

