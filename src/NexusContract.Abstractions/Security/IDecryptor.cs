namespace PubSoft.NexusContract.Abstractions.Security
{
    /// <summary>
    /// 解密器接口：与 IEncryptor 对称，用于 ResponseHydrationEngine
    /// 
    /// 设计原则：
    /// - 与 IEncryptor 完全对称
    /// - 解密行为由 ResponseHydrationEngine 调用（不在 Attribute 中）
    /// - 通常由同一个实现类同时实现 IEncryptor 和 IDecryptor
    /// </summary>
    public interface IDecryptor
    {
        /// <summary>
        /// 解密字段值
        /// </summary>
        /// <param name="cipherText">密文</param>
        /// <returns>明文</returns>
        string Decrypt(string cipherText);
    }
}
