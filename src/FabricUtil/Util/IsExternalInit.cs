// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Allows "init" properties in .NET Core 3.1.
    /// See <see href="https://github.com/microsoft/reverse-proxy/blob/f4ebaaf60c7194235a73b6131842011e0670bb78/src/ReverseProxy/Utilities/IsExternalInit.cs#L7"/>.
    /// </summary>
    /// <remarks>
    /// Notes from Jared Parsons from the .NET team indicating there is no need to #if-guard this to only older frameworks:
    /// "
    ///     It�s not a concern for this to be manually defined for net5.0. The compiler can handle it just fine. There is a small bug we had in 16.8 about ambiguities when we find several of these in a specific config but we fixed that in 16.9.
    ///     Also, make the type public not internal. It�s a part of your method signature. You can get into a couple of strange corner cases if you don�t do this.
    /// ".
    /// </remarks>
    public static class IsExternalInit
    {
    }
}
