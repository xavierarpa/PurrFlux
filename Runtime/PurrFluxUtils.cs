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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;

namespace PurrFlux
{
    public static class PurrFluxUtils
    {
        private static bool isSusbscribed = false;
        private static readonly Dictionary<string, List<Action<byte[]>>> listeners = new();
        private static readonly Dictionary<object, Action<byte[]>> wrappedHandlers = new();
        private static readonly BindingFlags allBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<Type, List<(MethodInfo method, string topic, Type paramType)>> purrFluxMethodCache = new();
        private static readonly Dictionary<(object instance, MethodInfo method), Delegate> purrFluxDelegateCache = new();
        private static NetworkManager NetworkManager => NetworkManager.main;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void OnAfterAssembliesLoaded()
        {
            HandleSubscription();
        }

        private static void HandleSubscription()
        {
            void OnNetworkReceived(PlayerID sender, PurrMessage msg, bool asServer)
            {
                Debug.Log($"[PurrFlux] (OnNetworkReceived) Received message on topic '{msg.topic}' from {sender} (asServer={asServer})");

                if (asServer)
                {
                    NetworkManager.SendToAll(msg, Channel.ReliableOrdered);
                }
                else
                {
                    if (listeners.TryGetValue(msg.topic, out var list))
                    {
                        foreach (var handler in list)
                        {
                            try
                            {
                                handler.Invoke(msg.payload);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogException(ex);
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[PurrFlux] No listeners for topic '{msg.topic}'");
                    }
                }
            }

            void TrySubscribe()
            {
                if (isSusbscribed)
                {
                    return;
                }

                if (NetworkManager == null)
                {
                    return;
                }

                NetworkManager.Subscribe<PurrMessage>(OnNetworkReceived);
                isSusbscribed = true;
            }

            void TryUnsubscribe()
            {
                if (!isSusbscribed)
                {
                    return;
                }

                if (NetworkManager != null)
                {
                    NetworkManager.Unsubscribe<PurrMessage>(OnNetworkReceived);
                }

                isSusbscribed = false;
            }

            void OnClientConnectionState(ConnectionState state)
            {
                if (state == ConnectionState.Connected)
                {
                    TrySubscribe();
                }
                else if (state == ConnectionState.Disconnected)
                {
                    TryUnsubscribe();
                }
            }

            TrySubscribe();
            NetworkManager.onAnyClientConnectionState += OnClientConnectionState;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="topic"></param>
        /// <param name="data"></param>
        /// <typeparam name="T"></typeparam>
        public static void DispatchNet<T>(this string topic, T data)
        {
            void Send(string topic, byte[] payload)
            {
                if (NetworkManager == null)
                {
                    Debug.LogWarning("[PurrFlux] Cannot publish: NetworkManager is null.");
                    return;
                }

                if (NetworkManager.isOffline)
                {
                    Debug.LogWarning("[PurrFlux] Cannot publish: not connected.");
                    return;
                }

                var message = new PurrMessage(topic, payload);

                if (NetworkManager.isServer)
                {
                    Debug.Log($"[PurrFlux] Publish Send To All Clients. topic:'{message.topic}'");
                    NetworkManager.SendToAll(message, Channel.ReliableOrdered);
                }
                else
                {
                    Debug.Log($"[PurrFlux] Publish Send To Server. topic:'{message.topic}'");
                    NetworkManager.SendToServer(message, Channel.ReliableOrdered);
                }
            }
            
            byte[] Serialize(T data)
            {
                using var stream = BitPackerPool.Get();
                Packer<T>.Write(stream, data);
                var byteData = stream.ToByteData();
                var payload = new byte[byteData.length];
                Buffer.BlockCopy(byteData.data, byteData.offset, payload, 0, byteData.length);
                return payload;
            }

            Send(topic, Serialize(data));
        }

        /// <summary>
        /// Publish an event with no payload (Action-style).
        /// </summary>
        public static void DispatchNet(this string topic)
        {
            topic.DispatchNet<byte>(0);
        }

        /// <summary>
        /// Listen for a topic with no payload (Action-style).
        /// </summary>
        private static void Listen(string topic, Action handler)
        {
            void HandlerWrapper(byte[] payload)
            {
                handler.Invoke();
            }

            void ListenTopic(string topic, Action<byte[]> handler)
            {
                if (!listeners.TryGetValue(topic, out var list))
                {
                    list = new List<Action<byte[]>>();
                    listeners[topic] = list;
                }

                if (!list.Contains(handler))
                {
                    list.Add(handler);
                }
            }

            wrappedHandlers[handler] = HandlerWrapper;
            ListenTopic(topic, wrappedHandlers[handler]);
        }

        /// <summary>
        /// Unlisten a topic with no payload (Action-style).
        /// </summary>
        private static void Unlisten(string topic, Action handler)
        {
            if (wrappedHandlers.TryGetValue(handler, out var wrapper))
            {
                if (listeners.TryGetValue(topic, out var list))
                {
                    list.Remove(wrapper);
                    if (list.Count == 0)
                    {
                        listeners.Remove(topic);
                    }
                }

                wrappedHandlers.Remove(handler);
            }
        }

        /// <summary>
        /// Unlisten a topic with typed payload.
        /// </summary>
        private static void Unlisten<T>(string topic, Action<T> handler)
        {
            if (wrappedHandlers.TryGetValue(handler, out var wrapper))
            {
                if (listeners.TryGetValue(topic, out var list))
                {
                    list.Remove(wrapper);
                    if (list.Count == 0)
                    {
                        listeners.Remove(topic);
                    }
                }

                wrappedHandlers.Remove(handler);
            }
        }

        /// <summary>
        /// Listen for a topic with typed payload.
        /// </summary>
        private static void Listen<T>(string topic, Action<T> handler)
        {
            T Deserialize(byte[] payload)
            {
                using var stream = BitPackerPool.Get(payload);
                return Packer<T>.Read(stream);
            }
            void HandlerWrapper(byte[] payload)
            {
                handler.Invoke(Deserialize(payload));
            }
            
            void ListenTopic(string topic, Action<byte[]> handler)
            {
                if (!listeners.TryGetValue(topic, out var list))
                {
                    list = new List<Action<byte[]>>();
                    listeners[topic] = list;
                }

                if (!list.Contains(handler))
                {
                    list.Add(handler);
                }
            }

            wrappedHandlers[handler] = HandlerWrapper;
            ListenTopic(topic, wrappedHandlers[handler]);
        }



        /// <summary>
        /// Subscribe/unsubscribe a parameterless Action to a topic.
        /// </summary>
        public static void StoreNet(this string topic, Action handler, bool condition)
        {
            if (condition)
            {
                Listen(topic, handler);
            }
            else
            {
                Unlisten(topic, handler);
            }
        }

        /// <summary>
        /// Subscribe/unsubscribe a typed Action&lt;T&gt; to a topic.
        /// </summary>
        public static void StoreNet<T>(this string topic, Action<T> handler, bool condition)
        {
            if (condition)
            {
                Listen(topic, handler);
            }
            else
            {
                Unlisten(topic, handler);
            }
        }

        /// <summary>
        /// Descubre métodos marcados con [MethodPurrFlux] y los suscribe/desuscribe
        /// como listeners de red. Al recibir datos, el HandlerWrapper de Listen
        /// hace Dispatch para que UniFlux entregue a los métodos atribuidos.
        /// </summary>
        public static void SubscribeAttributes<T>(in T obj, in bool condition) where T : class
        {
            var type = obj.GetType();

            if (!purrFluxMethodCache.TryGetValue(type, out var methodList))
            {
                methodList = new List<(MethodInfo, string, Type)>();
                var methods = type.GetMethods(allBindingFlags);

                for (int i = 0; i < methods.Length; i++)
                {
                    var methodAttr = (MethodPurrFluxAttribute)Attribute.GetCustomAttribute(methods[i], typeof(MethodPurrFluxAttribute));

                    if (methodAttr == null)
                    {
                        continue;
                    }

                    var parameters = methods[i].GetParameters();

                    if (parameters.Length > 1 || methods[i].ReturnType != typeof(void))
                    {
                        Debug.LogError($"[PurrFlux] '{methods[i].Name}' must have 0 or 1 parameters and return void.");
                        continue;
                    }

                    if (!(methodAttr.key is string topic) || string.IsNullOrEmpty(topic))
                    {
                        Debug.LogError($"[PurrFlux] '{methods[i].Name}' must have a non-empty string key.");
                        continue;
                    }

                    var paramType = parameters.Length == 1 ? parameters[0].ParameterType : null;
                    methodList.Add((methods[i], topic, paramType));
                }

                purrFluxMethodCache[type] = methodList;
            }

            for (int i = 0; i < methodList.Count; i++)
            {
                var (method, topic, paramType) = methodList[i];
                var delegateKey = (obj, method);

                if (!purrFluxDelegateCache.TryGetValue(delegateKey, out var del))
                {
                    if (paramType != null)
                    {
                        var actionType = typeof(Action<>).MakeGenericType(paramType);
                        del = Delegate.CreateDelegate(actionType, obj, method);
                    }
                    else
                    {
                        del = Delegate.CreateDelegate(typeof(Action), obj, method);
                    }

                    purrFluxDelegateCache[delegateKey] = del;
                }

                if (paramType != null)
                {
                    var storeMethod = typeof(PurrFluxUtils).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .First(m => m.Name == nameof(StoreNet) && m.IsGenericMethodDefinition)
                        .MakeGenericMethod(paramType);
                    storeMethod.Invoke(null, new object[] { topic, del, condition });
                }
                else
                {
                    StoreNet(topic, (Action)del, condition);
                }

                if (!condition)
                {
                    purrFluxDelegateCache.Remove(delegateKey);
                }
            }
        }

    }
}