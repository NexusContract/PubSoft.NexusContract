namespace NexusContract.Abstractions.Security
{
    /// <summary>
    /// 加密器接口：解耦加密算法实现
    /// 注意：Attribute 内部严禁包含 Encrypt() 方法，加密行为由 ProjectionEngine 调用
    /// </summary>
    public interface IEncryptor
    {
        /// <summary>
        /// 加密字段值
        /// </summary>
        /// <param name="plainText">明文</param>
        /// <returns>密文</returns>
        string Encrypt(string plainText);
    }
}


