#!/usr/bin/pwsh
<#
.SYNOPSIS
Script to crawl AniList for character names
.PARAMETER output
Output filename.
Default is names_anilist.txt.
#>

param(
    [string]$output = 'names_anilist.txt'
)

# disable extra noise for invoke-webrequest
#$ProgressPreference = "SilentlyContinue"

$requestTemplate = '{ "query": "query { Page (page: {0}, perPage: 50) { characters { name { full } } pageInfo { hasNextPage lastPage } } }" }'
$startTime = [DateTime]::UtcNow
$page = 0
$result = @()
$hasNextPage = $false
do
{
    try
    {
        $json = $requestTemplate.Replace('{0}', $page)
        $response = Invoke-RestMethod 'https://graphql.anilist.co' -Method Post -Body $json -ContentType "application/json"
        $response = $response.data.Page
        $hasNextPage = $response.pageInfo.hasNextPage
        $chars = $response.characters
        foreach ($char in $chars)
        {
            $result += $char.name.full.Trim()
        }

        $page++
        $total = $response.pageInfo.lastPage
        $remainingSeconds = ($total - $page) * 1.7
        if ($page -gt 100)
        {
            $remainingSeconds = ([DateTime]::UtcNow - $startTime).TotalSeconds / $page * ($total - $page)
        }
        Write-Progress Downloading -CurrentOperation "Page $page out of $total" -PercentComplete ($page * 100 / $total) -SecondsRemaining $remainingSeconds
        Start-Sleep -Seconds 1
    }
    catch
    {
        Write-Host "Failed to request page $page"
        $hasNextPage = $false
    }    
} while ($hasNextPage)
Write-Progress Downloading -Completed

Write-Host 'Saving the results...'
'# https://anilist.co/search/characters' | Out-File -LiteralPath $output
$result | Sort-Object | Get-Unique | Out-File -LiteralPath $output -Append

Write-Host 'Done.'