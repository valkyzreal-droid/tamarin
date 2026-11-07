# Measures Tamarin idle memory and CPU. Run Tamarin.exe first, leave it idle (window hidden) for 1 minute.
$p = Get-Process Tamarin -ErrorAction Stop
$c0 = $p.TotalProcessorTime; Start-Sleep 30; $p.Refresh()
$cpu = ($p.TotalProcessorTime - $c0).TotalSeconds / 30 / [Environment]::ProcessorCount * 100
"{0:N1} MB working set | {1:N1} MB private | CPU {2:N2}% (30 s idle avg) | threads {3}" -f ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB), $cpu, $p.Threads.Count
