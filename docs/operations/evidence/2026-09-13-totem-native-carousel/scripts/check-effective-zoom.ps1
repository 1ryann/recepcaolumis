$ErrorActionPreference = 'Stop'
function Ab { agent-browser skills get core --full | Out-Null; & agent-browser --session lumis-task2-zoom @args }
$evidenceRoot = Split-Path $PSScriptRoot -Parent
$people = Get-Content -Raw (Join-Path $evidenceRoot 'fixtures\professionals-fixture.json')

# CDP key dispatch cannot change Chrome's UI zoom. Record that limitation first.
Ab set viewport 1920 1080 1
Ab set media dark no-preference
Ab network route '**/api/totem/professionals' --body $people
Ab reload
Ab wait --text 'Carla Reis'
Ab focus '.totem-carousel-viewport'
foreach ($step in 1..5) { Ab press Control+plus }
Write-Output 'Ctrl+plus capability check (not Chrome UI zoom)'
Ab eval "JSON.stringify({innerWidth,innerHeight,devicePixelRatio,visualViewportScale:visualViewport.scale,mediaMax560:matchMedia('(max-width: 560px)').matches,reducedMotion:matchMedia('(prefers-reduced-motion: reduce)').matches})"

# CDP emulates the CSS viewport and DPR produced by a 1920x1080, DPR 1 display at 200% Chrome browser zoom.
# It is an effective-viewport equivalent, not a literal Chrome UI zoom operation.
Ab set viewport 960 540 2
Ab focus '.totem-carousel-viewport'
Ab press Home
Ab wait --fn 'Math.abs(document.querySelector(".is-active[role=option]").getBoundingClientRect().x + document.querySelector(".is-active[role=option]").getBoundingClientRect().width/2 - innerWidth/2) < 4'
Write-Output 'Effective 200 percent browser-zoom equivalent (960x540 CSS viewport, DPR 2)'
Ab eval "JSON.stringify((()=>{const d=document.documentElement,a=document.querySelector('.is-active[role=option]'),r=a.getBoundingClientRect();return {innerWidth,innerHeight,devicePixelRatio,visualViewportScale:visualViewport.scale,mediaMax560:matchMedia('(max-width: 560px)').matches,reducedMotion:matchMedia('(prefers-reduced-motion: reduce)').matches,documentWidth:d.scrollWidth,clientWidth:d.clientWidth,horizontalOverflow:d.scrollWidth>d.clientWidth,documentHeight:d.scrollHeight,clientHeight:d.clientHeight,scrollY,focus:document.activeElement?.getAttribute('aria-label'),focusVisible:document.activeElement?.matches(':focus-visible'),activeTransform:getComputedStyle(a).transform,centerError:+(r.x+r.width/2-innerWidth/2).toFixed(2)}})())"
Ab screenshot (Join-Path $evidenceRoot 'screenshots\after-effective-200-browser-zoom-equivalent.png')
Ab scroll down 500
Ab wait --fn 'scrollY > 0'
Write-Output 'Effective equivalent after vertical scroll'
Ab eval "JSON.stringify((()=>{const d=document.documentElement;return {innerWidth,innerHeight,devicePixelRatio,documentWidth:d.scrollWidth,clientWidth:d.clientWidth,horizontalOverflow:d.scrollWidth>d.clientWidth,documentHeight:d.scrollHeight,clientHeight:d.clientHeight,scrollY,focus:document.activeElement?.getAttribute('aria-label'),focusVisible:document.activeElement?.matches(':focus-visible'),reducedMotion:matchMedia('(prefers-reduced-motion: reduce)').matches}})())"
Ab screenshot (Join-Path $evidenceRoot 'screenshots\after-effective-200-browser-zoom-equivalent-scrolled.png')
