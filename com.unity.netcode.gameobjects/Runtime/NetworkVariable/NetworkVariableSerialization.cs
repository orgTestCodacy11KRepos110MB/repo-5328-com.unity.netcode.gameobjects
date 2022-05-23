using System;
using Unity.Collections;

namespace Unity.Netcode
{
    /// <summary>
    /// Support methods for reading/writing NetworkVariables
    /// Because there are multiple overloads of WriteValue/ReadValue based on different generic constraints,
    /// but there's no way to achieve the same thing with a class, this includes various read/write delegates
    /// based on which constraints are met by `T`. These constraints are set up by `NetworkVariableHelpers`,
    /// which is invoked by code generated by ILPP during module load.
    /// (As it turns out, IL has support for a module initializer that C# doesn't expose.)
    /// This installs the correct delegate for each `T` to ensure that each type is serialized properly.
    ///
    /// Any type that inherits from `NetworkVariableSerialization<T>` will implicitly result in any `T`
    /// passed to it being picked up and initialized by ILPP.
    ///
    /// The methods here, despite being static, are `protected` specifically to ensure that anything that
    /// wants access to them has to inherit from this base class, thus enabling ILPP to find and initialize it.
    /// </summary>
    [Serializable]
    public abstract class NetworkVariableSerialization<T> : NetworkVariableBase where T : unmanaged
    {
        // Functions that know how to serialize INetworkSerializable
        internal static void WriteNetworkSerializable<TForMethod>(FastBufferWriter writer, in TForMethod value)
            where TForMethod : unmanaged, INetworkSerializable
        {
            writer.WriteNetworkSerializable(value);
        }

        internal static void ReadNetworkSerializable<TForMethod>(FastBufferReader reader, out TForMethod value)
            where TForMethod : unmanaged, INetworkSerializable
        {
            reader.ReadNetworkSerializable(out value);
        }

        // Functions that serialize structs
        internal static void WriteStruct<TForMethod>(FastBufferWriter writer, in TForMethod value)
            where TForMethod : unmanaged, INetworkSerializeByMemcpy
        {
            writer.WriteValueSafe(value);
        }
        internal static void ReadStruct<TForMethod>(FastBufferReader reader, out TForMethod value)
            where TForMethod : unmanaged, INetworkSerializeByMemcpy
        {
            reader.ReadValueSafe(out value);
        }

        // Functions that serialize enums
        internal static void WriteEnum<TForMethod>(FastBufferWriter writer, in TForMethod value)
            where TForMethod : unmanaged, Enum
        {
            writer.WriteValueSafe(value);
        }
        internal static void ReadEnum<TForMethod>(FastBufferReader reader, out TForMethod value)
            where TForMethod : unmanaged, Enum
        {
            reader.ReadValueSafe(out value);
        }

        // Functions that serialize other types
        internal static void WritePrimitive<TForMethod>(FastBufferWriter writer, in TForMethod value)
            where TForMethod : unmanaged, IComparable, IConvertible, IComparable<TForMethod>, IEquatable<TForMethod>
        {
            writer.WriteValueSafe(value);
        }

        internal static void ReadPrimitive<TForMethod>(FastBufferReader reader, out TForMethod value)
            where TForMethod : unmanaged, IComparable, IConvertible, IComparable<TForMethod>, IEquatable<TForMethod>
        {
            reader.ReadValueSafe(out value);
        }

        // There are many FixedString types, but all of them share the interfaces INativeList<bool> and IUTF8Bytes.
        // INativeList<bool> provides the Length property
        // IUTF8Bytes provides GetUnsafePtr()
        // Those two are necessary to serialize FixedStrings efficiently
        // - otherwise we'd just be memcpying the whole thing even if
        // most of it isn't used.
        internal static void WriteFixedString<TForMethod>(FastBufferWriter writer, in TForMethod value)
            where TForMethod : unmanaged, INativeList<byte>, IUTF8Bytes
        {
            writer.WriteValueSafe(value);
        }

        internal static void ReadFixedString<TForMethod>(FastBufferReader reader, out TForMethod value)
            where TForMethod : unmanaged, INativeList<byte>, IUTF8Bytes
        {
            reader.ReadValueSafe(out value);
        }

        // Should never be reachable at runtime. All calls to this should be replaced with the correct
        // call above by ILPP.
        private static void WriteValue<TForMethod>(FastBufferWriter writer, in TForMethod value)
            where TForMethod : unmanaged
        {
            if (value is INetworkSerializable)
            {
                typeof(NetworkVariableHelper).GetMethod(nameof(NetworkVariableHelper.InitializeDelegatesNetworkSerializable)).MakeGenericMethod(typeof(TForMethod)).Invoke(null, null);
            }
            else if (value is INetworkSerializeByMemcpy)
            {
                typeof(NetworkVariableHelper).GetMethod(nameof(NetworkVariableHelper.InitializeDelegatesStruct)).MakeGenericMethod(typeof(TForMethod)).Invoke(null, null);
            }
            else if (value is Enum)
            {
                typeof(NetworkVariableHelper).GetMethod(nameof(NetworkVariableHelper.InitializeDelegatesEnum)).MakeGenericMethod(typeof(TForMethod)).Invoke(null, null);
            }
            else
            {
                throw new Exception($"Type {typeof(T).FullName} is not serializable - it must implement either INetworkSerializable or ISerializeByMemcpy");

            }
            NetworkVariableSerialization<TForMethod>.Write(writer, value);
        }

        private static void ReadValue<TForMethod>(FastBufferReader reader, out TForMethod value)
            where TForMethod : unmanaged
        {
            if (typeof(INetworkSerializable).IsAssignableFrom(typeof(TForMethod)))
            {
                typeof(NetworkVariableHelper).GetMethod(nameof(NetworkVariableHelper.InitializeDelegatesNetworkSerializable)).MakeGenericMethod(typeof(TForMethod)).Invoke(null, null);
            }
            else if (typeof(INetworkSerializeByMemcpy).IsAssignableFrom(typeof(TForMethod)))
            {
                typeof(NetworkVariableHelper).GetMethod(nameof(NetworkVariableHelper.InitializeDelegatesStruct)).MakeGenericMethod(typeof(TForMethod)).Invoke(null, null);
            }
            else if (typeof(Enum).IsAssignableFrom(typeof(TForMethod)))
            {
                typeof(NetworkVariableHelper).GetMethod(nameof(NetworkVariableHelper.InitializeDelegatesEnum)).MakeGenericMethod(typeof(TForMethod)).Invoke(null, null);
            }
            else
            {
                throw new Exception($"Type {typeof(T).FullName} is not serializable - it must implement either INetworkSerializable or ISerializeByMemcpy");

            }
            NetworkVariableSerialization<TForMethod>.Read(reader, out value);
        }

        protected internal delegate void WriteDelegate<TForMethod>(FastBufferWriter writer, in TForMethod value);

        protected internal delegate void ReadDelegate<TForMethod>(FastBufferReader reader, out TForMethod value);

        // These static delegates provide the right implementation for writing and reading a particular network variable type.
        // For most types, these default to WriteValue() and ReadValue(), which perform simple memcpy operations.
        //
        // INetworkSerializableILPP will generate startup code that will set it to WriteNetworkSerializable()
        // and ReadNetworkSerializable() for INetworkSerializable types, which will call NetworkSerialize().
        //
        // In the future we may be able to use this to provide packing implementations for floats and integers to optimize bandwidth usage.
        //
        // The reason this is done is to avoid runtime reflection and boxing in NetworkVariable - without this,
        // NetworkVariable would need to do a `var is INetworkSerializable` check, and then cast to INetworkSerializable,
        // *both* of which would cause a boxing allocation. Alternatively, NetworkVariable could have been split into
        // NetworkVariable and NetworkSerializableVariable or something like that, which would have caused a poor
        // user experience and an API that's easier to get wrong than right. This is a bit ugly on the implementation
        // side, but it gets the best achievable user experience and performance.
        private static WriteDelegate<T> s_Write = WriteValue;
        private static ReadDelegate<T> s_Read = ReadValue;

        protected static void Write(FastBufferWriter writer, in T value)
        {
            s_Write(writer, value);
        }

        protected static void Read(FastBufferReader reader, out T value)
        {
            s_Read(reader, out value);
        }

        internal static void SetWriteDelegate(WriteDelegate<T> write)
        {
            s_Write = write;
        }

        internal static void SetReadDelegate(ReadDelegate<T> read)
        {
            s_Read = read;
        }

        protected NetworkVariableSerialization(
            NetworkVariableReadPermission readPerm = DefaultReadPerm,
            NetworkVariableWritePermission writePerm = DefaultWritePerm)
            : base(readPerm, writePerm)
        {
        }
    }
}