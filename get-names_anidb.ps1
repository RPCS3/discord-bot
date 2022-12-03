#!/usr/bin/pwsh
<#
.SYNOPSIS
Script to crawl AniDB for character names
.PARAMETER includeMecha
If enabled, include mecha names in the list.
Default is not enabled.
.PARAMETER startpage
Letter page to start from. Asume default number of rows per page.
Default is 0.
.PARAMETER output
Output filename.
Default is names_anidb.txt.
#>
param(
    [switch]$includeMecha = $false,
    [int]$startpage = 0,
    [string]$output = 'names_anidb.txt'
)

# disable extra noise for invoke-webrequest
#$ProgressPreference = "SilentlyContinue"

$page = $startpage
$total = 3971
$result = @()
$hasNextPage = $false

# get anonymous sesssion
Invoke-WebRequest "https://anidb.net" -SessionVariable 'Session' | Out-Null
Start-Sleep -Seconds 2

function Request-Page($num)
{
    # all template: https://anidb.net/character/?noalias=1&orderby.name=0.1&page=1&view=list
    # letter template: https://anidb.net/character/?char=a&noalias=1&orderby.name=0.1&page=1&view=list
    $url = "https://anidb.net/character/?noalias=1&orderby.name=0.1&page=$num&view=list"
    $response = Invoke-WebRequest $url -WebSession $Session
    #$links = @($response.links | Where-Object { $_.href -match '/character/\d+$' } | Where-Object { $_.outerHTML -match '^<a [^>]+>[^<][^\n]+</a>$' })
    Start-Sleep -Seconds 3
    return $response.Content
}

function Get-TotalPageCount($start)
{
    $min = $start
    $max = $start + 1000
    do
    {
        $html = Request-Page $max
        Write-Progress -Activity "Downloading" -Status "Counting pages... [$min - $max]"
        $max += 1000
    } while ($html.contains('>next</a></li>'))

    while ($min -lt $max)
    {
        $middle = $min + [int](($max - $min) / 2)
        $html = Request-Page $middle
        if ($html.Contains('>next</a></li>'))
        {
            $min = $middle
        }
        elseif ($html.Contains('<div class="container">No results.'))
        {
            $max = $middle
        }
        else
        {
            return $middle
        }
        Write-Progress -Activity "Downloading" -Status "Counting pages... [$min - $max]"
    }
    return $middle
}

try
{
    Write-Progress -Activity "Downloading" -Status "Counting pages..." -PercentComplete 0
    $total = Get-TotalPageCount $startPage
}
catch
{
    Write-Warning "Failed to count total number of pages, using default [$total]"
}

$startTime = [DateTime]::UtcNow
$remainingSeconds = ($total - $page) * 1.7

do
{
    try
    {
        Write-Progress -Activity "Downloading" -Status "Page $page of $total" -PercentComplete ($page * 100.0 / $total) -SecondsRemaining $remainingSeconds
        $html = Request-Page $page
        $hasNextPage = $html.Contains('>next</a></li>')
        $pos = $html.IndexOf('<table class="characterlist">')
        if ($pos -lt 1)
        {
            if ($html.Contains('Please unban me'))
            {
                Write-Host 'Saving the results...'
                '# https://anidb.net/character' | Out-File -LiteralPath $output
                $result | Sort-Object | Get-Unique | Out-File -LiteralPath $output -Append
                Read-Host -Prompt 'Rate limited, plz unban...'
                continue
            }
            else
            {
                Write-Host $html
                Write-Error "This script needs updating"
                exit -1
            }
        }

        do
        {
            $pos = $html.IndexOf('<td data-label="Title"', $pos)
            if ($pos -lt 0)
            {
                break
            }
            
            $pos = $html.IndexOf('<a href=', $pos)
            $pos = $html.IndexOf('>', $pos)
            $endPos = $html.IndexOf('</a></td>', $pos)
            $name = $html.Substring($pos + 1, $endPos - $pos - 1).Replace('  ', ' ').Trim()

            $pos = $html.IndexOf('<td data-label="Type"', $endPos)
            $pos = $html.IndexOf('>', $pos)
            $endPos = $html.IndexOf('</td>', $pos)
            $type = $html.Substring($pos + 1, $endPos - $pos - 1)

            if (($type -ieq 'Character') -or ($includeMecha -and ($type -ieq 'Mecha')))
            {
                if (($name.Length -lt 2) -or ("$name" -match '^\d+$'))
                {
                    Write-Host "Skipping $name"
                    continue 
                }

                $result += $name
            }
            else
            {
                Write-Host "Skipping $name ($type)"
            }
        } until ($pos -lt 0)
        
        $page++
        $total = [Math]::Max($total, $response.pageInfo.lastPage)
        if (($page - $startPage) -gt 60)
        {
            $remainingSeconds = ([DateTime]::UtcNow - $startTime).TotalSeconds / ($page - $startPage) * ($total - $page)
        }
        else
        {
            $remainingSeconds = ($total - $page) * 1.7
        }
    }
    catch
    {
        Write-Host "Failed to fetch the page" -ForegroundColor Yellow
        $hasNextPage = $false
    }
} while ($hasNextPage)
Write-Host "Stopped on page $page"
Write-Progress -Activity "Downloading" -Completed

Write-Host 'Saving the results...'
'# https://anidb.net/character' | Out-File -LiteralPath $output
$result | Sort-Object | Get-Unique | Out-File -LiteralPath $output -Append

Write-Host 'Done.'