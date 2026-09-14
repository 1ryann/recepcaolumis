$ErrorActionPreference = 'Stop'
function Ab { agent-browser skills get core --full | Out-Null; & agent-browser --session lumis-task2 @args }

$evidenceRoot = Split-Path $PSScriptRoot -Parent
$people = Get-Content -Raw (Join-Path $evidenceRoot 'fixtures\professionals-fixture.json')
function Metrics {
  $metricCode = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Content -Raw (Join-Path $PSScriptRoot 'metrics.js'))))
  agent-browser skills get core --full | Out-Null
  agent-browser --session lumis-task2 eval -b $metricCode
}
function WaitForSettledActive([string] $expected) {
  Ab wait --fn "(()=>{const a=document.querySelector('.is-active[role=option]');if(!a)return false;const r=a.getBoundingClientRect();return a.querySelector('.totem-carousel-name')?.textContent === '$expected' && Math.abs(r.x+r.width/2-innerWidth/2) < 4 && getComputedStyle(a).transform.startsWith('matrix(')})()"
}

Ab network route '**/api/totem/professionals' --body $people
Ab reload
Ab wait --text 'Ana Souza'

$keyboardCases = @(
  @{ key = 'Home'; expected = 'Ana Souza' },
  @{ key = 'End'; expected = 'Elisa Martins' },
  @{ key = 'ArrowLeft'; expected = 'Diego Santos' },
  @{ key = 'ArrowRight'; expected = 'Elisa Martins' },
  @{ key = 'Home'; expected = 'Ana Souza' }
)
foreach ($size in @(@(1920,1080), @(1366,768), @(768,1024), @(390,844), @(320,568))) {
  Ab set viewport $size[0] $size[1]
  Ab focus '.totem-carousel-viewport'
  foreach ($case in $keyboardCases) {
    Ab press $case.key
    WaitForSettledActive $case.expected
    Write-Output "Keyboard: $($case.key), settled active: $($case.expected)"
    Metrics
  }
}

Ab set viewport 390 600
Ab eval 'window.scrollTo(0,0)'
Write-Output 'Short mobile before vertical wheel'
Metrics
Ab mouse move 195 400
Ab mouse wheel 500
Ab wait --fn 'scrollY > 0'
Write-Output 'Short mobile after vertical wheel on card'
Metrics
Ab screenshot (Join-Path $evidenceRoot 'screenshots\after-390x600-scrolled.png')

Ab set viewport 390 844
Ab set media dark reduced-motion
Ab focus '.totem-carousel-viewport'
Ab press End
Ab wait --fn "(()=>{const a=document.querySelector('.is-active[role=option]');if(!a)return false;const r=a.getBoundingClientRect();return a.querySelector('.totem-carousel-name')?.textContent === 'Elisa Martins' && Math.abs(r.x+r.width/2-innerWidth/2) < 4 && getComputedStyle(a).transform === 'none'})()"
Write-Output 'Reduced motion End, settled active: Elisa Martins'
Metrics
Ab screenshot (Join-Path $evidenceRoot 'screenshots\after-390x844-reduced-motion.png')
Ab set media dark no-preference
