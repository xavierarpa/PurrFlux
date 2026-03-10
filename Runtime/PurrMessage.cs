/*
Copyright (c) 2026 Xavier Arpa López Thomas Peter ('xavierarpa')

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
using System;
using PurrNet.Packing;
namespace PurrFlux
{
    [Serializable]
    internal readonly struct PurrMessage : IPackedAuto
    {
        /// <summary>
        /// 
        /// </summary>
        public readonly string topic; // Pensar alternativa con int para ahorrrar?

        /// <summary>
        /// The payload of the message, serialized as a byte array.
        /// </summary>
        public readonly byte[] payload;
        
        /// <summary>
        /// Indicates whether the message should be sent only to the server.
        /// </summary>
        public readonly bool serverOnly;

        public PurrMessage(string topic, byte[] payload, bool serverOnly)
        {
            this.topic = topic;
            this.payload = payload;
            this.serverOnly = serverOnly;
        }
    }    
}
