<#
.SYNOPSIS
    ACLs 'NT AUTHORITY\NETWORK SERVICE' for the list of certificate subject Names.

.PARAMETER  CertificateSubjectNameDnsZonesToACLToNetworkService
    List of Certificate Subject Name Dns Zones to ACL to Network Service.
#>

param(
    [parameter(Mandatory = $true)]
    [string] $CertificateSubjectNameDnsZonesToACLToNetworkService
)

$ErrorActionPreference = "Stop"

function Acl-Cert ($CertificateSubjectNameDnsZonesToACLToNetworkService, $user) {
    Write-Output "Starting ACLCertificates.ps1: $([System.DateTime]::UtcNow)"
    Write-Output "Certificate subject name dns zones to ACL : $CertificateSubjectNameDnsZonesToACLToNetworkService"
    $UserPrincipal=New-Object System.Security.Principal.WindowsPrincipal([System.Security.Principal.WindowsIdentity]::GetCurrent())
    $AdminTEST = $UserPrincipal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    Write-Output "Running as: $($UserPrincipal.Identities.Name)"
    Write-Output "IsElevated? = $AdminTEST"
    if([string]::IsNullOrEmpty($CertificateSubjectNameDnsZonesToACLToNetworkService))
    {
        return;
    }

    $dnsZones = $CertificateSubjectNameDnsZonesToACLToNetworkService.ToLowerInvariant().Split(",",[System.StringSplitOptions]::RemoveEmptyEntries).Trim()
    $certs = Get-ChildItem -Path Cert:\LocalMachine\My 
    foreach ($cert in $certs)
    {
        $commonName = Get-CommonName $cert.Subject
        if (-NOT $commonName)
        {
            Write-Output "Failed to get common name for certificate with thumbprint: $($cert.Thumbprint), subject name: $($cert.Subject)"
            continue;
        }

        foreach ($dnsZone in $dnsZones)
        {
            if ($commonName.EndsWith($dnsZone))
            {
                $keyname = $cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
                $keypath = $env:ProgramData + "\Microsoft\Crypto\RSA\MachineKeys\"
                $fullpath = $keypath+$keyname

                Write-Output "Updating acls on certificate with thumbprint: $($cert.Thumbprint), subject name: $($cert.Subject), key path: $fullpath due to dns zone: $dnsZone"

                icacls $fullpath /grant $user`:RX

                break;
            }
        }
    }
}

# Subject Name in prod : "CN=forintracommuseonly.prod.pacore.powerapps.com"
# Subject Name in Gov : "CN=forintracommuseonly.high.pacore.powerapps.us, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"
function Get-CommonName ($subjectName) {
    if([string]::IsNullOrEmpty($subjectName))
    {
        return;
    }

    $distinguishedNames = $subjectName.ToLowerInvariant().Split(',').Trim()
    $commonNamePrefix = "cn="
    foreach ($distinguishedName in $distinguishedNames)
    {
        if ($distinguishedName.StartsWith($commonNamePrefix))
        {
            return $distinguishedName.Substring($commonNamePrefix.Length).Trim()
        }
    }
}

try {
    Acl-Cert $CertificateSubjectNameDnsZonesToACLToNetworkService 'NT AUTHORITY\NETWORK SERVICE' >> $PSScriptRoot\ACLOutput.txt
}
catch {
    Write-Output $($_ | Out-String) >> $PSScriptRoot\ACLOutput.txt
    throw $_
}
