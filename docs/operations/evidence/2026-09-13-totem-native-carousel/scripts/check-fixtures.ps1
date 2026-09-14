$ErrorActionPreference = 'Stop'
function Ab { agent-browser skills get core --full | Out-Null; & agent-browser --session lumis-task2 @args }
$evidenceRoot = Split-Path $PSScriptRoot -Parent
function Metrics { $metricCode = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Content -Raw (Join-Path $PSScriptRoot 'metrics.js')))); agent-browser skills get core --full | Out-Null; agent-browser --session lumis-task2 eval -b $metricCode }
function WaitForSettledActive([string] $expected) {
  Ab wait --fn "(()=>{const a=document.querySelector('.is-active[role=option]');if(!a)return false;const r=a.getBoundingClientRect();return a.querySelector('.totem-carousel-name')?.textContent === '$expected' && Math.abs(r.x+r.width/2-innerWidth/2) < 4 && getComputedStyle(a).transform.startsWith('matrix(')})()"
}
$people = Get-Content -Raw (Join-Path $evidenceRoot 'fixtures\professionals-fixture.json') | ConvertFrom-Json
Ab set viewport 390 844
foreach ($count in @(0,1,2,5)) {
  $fixture = ConvertTo-Json -InputObject @($people | Select-Object -First $count) -Compress
  Ab network unroute '**/api/totem/professionals'
  Ab network route '**/api/totem/professionals' --body $fixture
  Ab reload
  if ($count -eq 0) { Ab wait --text 'Nenhum profissional disponível.' } else { Ab wait --text 'Ana Souza'; Ab focus '.totem-carousel-viewport'; Ab press End; WaitForSettledActive $people[($count - 1)].name }
  Write-Output "Fixture count: $count"
  Ab wait --fn "document.querySelectorAll('[role=option]').length === $count"
  Metrics
  Ab snapshot -i
  Ab screenshot (Join-Path $evidenceRoot "screenshots\after-390x844-count-$count.png")
}
