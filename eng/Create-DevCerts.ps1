# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

<#
.SYNOPSIS
Ensures that local certificates are created for local development.
This script runs successfully as non-elevated if all necessary artifacts are already installed,
but requires elevation if any changes are necessary.

#>

param(
    [Switch]
    $Quiet
)

$ErrorActionPreference = 'Stop'

$localhostIpAddress = "127.0.0.1"
$intracommcertname = 'intracomm.localhost'

$friendlyName = "SF-YARP development cert"
$validityInterval = [TimeSpan]::FromDays(365*100)
$renewInterval = [TimeSpan]::FromDays(14)

function Write-Info ($message) {
    if (-not $Quiet) {
        Write-Host $message
    }
}

function Get-Cert ($subjectName) {
    # Return IntraCommCert, if it exists
    dir cert:\localmachine\my `
        | ? { $_.Subject -eq "CN=$subjectName" } `
        | Sort-Object -Property 'NotAfter' -Descending `
        | Select-Object -First 1   
}

function Needs-Rotation ($certificate) {
    return ($certificate.NotAfter - [DateTime]::Now - $renewInterval).TotalDays -lt 0
}

function Create-Cert ($subjectName) {
    Write-Info "Creating $subjectName certificate"
    $certificate = New-SelfSignedCertificate `
        -CertStoreLocation cert:\localmachine\my `
        -DnsName $subjectName `
        -NotAfter ([DateTime]::Now + $validityInterval) `
        -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider' `
        -KeyExportPolicy Exportable `
        -FriendlyName $friendlyName
    return $certificate
}

function Save-Cert($certificate, $filepath) {
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($filepath)) | Out-Null
    Export-Certificate -Cert $certificate -FilePath $filepath | Out-Null
}

function Trust-Cert($certificateFilepath) {
    # Use with care. Adds this certificate to the machine's trusted store.
    # Note that aspnetcore also stores its localhost certificates in a similar manner, in both
    # LocalMachine\My and LocalMachine\Root.
    Import-Certificate `
        -CertStoreLocation cert:\LocalMachine\root `
        -FilePath $certificateFilepath | Out-Null
}

function Acl-Cert($certificates, $user) {
    # Note, only works on 'Microsoft Enhanced RSA and AES Cryptographic Provider' machine-level keys
    $certificates | % {
        $keyname = $_.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
        $keypath = $env:ProgramData + "\Microsoft\Crypto\RSA\MachineKeys\"
        $fullpath = $keypath+$keyname

        Write-Info "Updating acls on certificate $($_.Thumbprint), key path $($fullpath)"

        icacls $fullpath /grant $user`:RX
    }
}

function Ensure-HostsFileMapping($hostName) {
    if (!(Get-Content $env:windir\System32\drivers\etc\hosts | Select-String $hostName -quiet)) {
        Write-Info "Adding entry for $hostName in hosts file"
        Add-Content $env:windir\System32\drivers\etc\hosts "`n$localhostIpAddress $hostName"
    }
    else {
        Write-Info "Hosts file already has an entry for $hostName, not making any changes"
    }
}

function Ensure-Cert($subjectName) {
    $certificate = Get-Cert -subjectName $subjectName
    if ($certificate -and (-not (Needs-Rotation $certificate))) {
        Write-Info "Certificate already present for $subjectName"
    }
    else {
        if ($certificate) {
            Write-Info "Previous certificate for $subjectName $($certificate.Thumbprint) needs rotation, expires on $($certificate.NotAfter)"
        }

        $certificate = Create-Cert -subjectName $subjectName
        $certFilePath = "$env:OUTPUTROOT\data\$subjectName.cer" 
        Save-Cert $certificate $certFilePath
        Trust-Cert $certFilePath
        Acl-Cert $certificate 'NT AUTHORITY\NETWORK SERVICE'
    }
    Write-Info "-----Begin certificate information for $subjectName-----"
    Write-Info $certificate.ToString()
    Write-Info "-----End certificate information for $subjectName-----"
}

function Ensure-CertAndHostsFileMapping($subjectName) {
    Ensure-Cert -subjectName $subjectName
    Ensure-HostsFileMapping -hostName $subjectName
}


try {
    # Localhost cert
    Ensure-Cert -subjectName "localhost"
    Ensure-CertAndHostsFileMapping -subjectName $intracommcertname
    Ensure-CertAndHostsFileMapping -subjectName "echo.localhost"
}
catch {
    Write-Host -ForegroundColor Red $($_ | Out-String)
    Exit 1
}
