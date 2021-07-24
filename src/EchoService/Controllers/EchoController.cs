// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.AspNetCore.Mvc;

namespace EchoService.Controllers
{
    /// <summary>
    /// Echo controller.
    /// </summary>
    public class EchoController : Controller
    {
        /// <summary>
        /// Returns a dummy response.
        /// </summary>
        [HttpGet]
        [Route("/api/echo")]
        public IActionResult Echo()
        {
            return this.Ok(
                new EchoResponse
                {
                    Message = "Hello from EchoService",
                    ServerTime = new DateTimeOffset(DateTimeOffset.UtcNow.Ticks / 10000 * 10000, TimeSpan.Zero),
                });
        }

        /// <summary>
        /// The Echo response object.
        /// </summary>
        public class EchoResponse
        {
            /// <summary>
            /// Arbitrary message.
            /// </summary>
            public string Message { get; set; }

            /// <summary>
            /// EchoService server time.
            /// </summary>
            public DateTimeOffset ServerTime { get; set; }
        }
    }
}
