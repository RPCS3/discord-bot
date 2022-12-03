#!/usr/bin/pwsh
<#
.SYNOPSIS
Script to crawl AniList for character names
.PARAMETER startPage
Start page number.
Default is 0.
.PARAMETER output
Output filename.
Default is names_anilist.txt.
#>

param(
    [int]$startPage = 0,
    [string]$output = 'names_anilist.txt'
)

# disable extra noise for invoke-webrequest
#$ProgressPreference = "SilentlyContinue"

$requestTemplate = '{ "query": "query { Page (page: {0}, perPage: 50) { characters { name { full } } pageInfo { hasNextPage lastPage } } }" }'
$page = $startPage
$total = 2565
$result = @()
$hasNextPage = $false

function Request-Page($num)
{
    $json = $requestTemplate.Replace('{0}', $num)
    $response = Invoke-RestMethod 'https://graphql.anilist.co' -Method Post -Body $json -ContentType "application/json" -TimeoutSec 30
    Start-Sleep -Seconds 1.5
    return $response.data.Page
}

function Get-TotalPageCount($start)
{
    $min = $start
    $max = $start + 1000
    do
    {
        $response = Request-Page $max
        Write-Progress -Activity "Downloading" -Status "Counting pages... [$min - $max]"
        $max += 1000
    } while ($response.pageInfo.hasNextPage)

    while ($min -lt $max)
    {
        $middle = $min + [int](($max - $min) / 2)
        $response = Request-Page $middle
        if ($response.pageInfo.hasNextPage)
        {
            $min = $middle
        }
        elseif ($response.characters.Count -eq 0)
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
        $response = Request-Page $page
        $hasNextPage = $response.pageInfo.hasNextPage
        $chars = $response.characters
        foreach ($char in $chars)
        {
            $name = $char.name.full
            if ($null -eq $name) { continue }
            
            $name = $char.name.full.Replace('  ', ' ').Trim()
            if (($name.Length -lt 2) -or ("$name" -match '^\d+$'))
            {
                Write-Host "Skipping $name"
                continue 
            }
            $result += $name
        }

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
        Write-Error "Failed to request page $page`: $_"
        $hasNextPage = $false
    }    
} while ($hasNextPage)
Write-Host "Stopped on page $page"
Write-Progress -Activity "Downloading" -Completed

Write-Host 'Saving the results...'
'# https://anilist.co/search/characters' | Out-File -LiteralPath $output
$result | Sort-Object | Get-Unique | Out-File -LiteralPath $output -Append

Write-Host 'Done.'