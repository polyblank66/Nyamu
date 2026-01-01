using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class NyamuTests
{
    [Test]
    public void PassingTest1()
    {
        Assert.AreEqual(4, 2 + 2);
    }

    [Test]
    public void PassingTest2()
    {
        Assert.IsTrue(true);
    }

    [Test]
    public void FailingTest1()
    {
        Assert.AreEqual(5, 2 + 2, "This should fail: 2 + 2 = 4, not 5");
    }

    [Test]
    public void FailingTest2()
    {
        Assert.IsFalse(true, "This should fail: true is not false");
    }

    [Test]
    public void PassingTest3()
    {
        var vector = new Vector3(1, 2, 3);
        Assert.AreEqual(1, vector.x);
    }

    [Test]
    public void LargeErrorMessageTest()
    {
        // Generate a very large error message (approximately 50000 characters)
        var errorBuilder = new System.Text.StringBuilder();

        // Create a detailed error message that simulates a complex compilation error
        string baseErrorMessage = "Complex nested template instantiation error in file SomeVeryLongFileNameThatRepresentsARealWorldScenario.cs at line 1234: ";
        string detailedError = "Error CS0012: The type 'SomeGenericType<T, U, V, W>' is defined in an assembly that is not referenced. " +
                              "You must add a reference to assembly 'SomeAssembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'. " +
                              "This error occurred during template instantiation of class 'ComplexGenericClass<TypeA, TypeB, TypeC>' " +
                              "while trying to resolve method 'SomeMethod<TParam1, TParam2, TParam3>(TParam1 arg1, TParam2 arg2, TParam3 arg3)' " +
                              "in context of inheritance hierarchy involving interfaces 'IInterface1<T>', 'IInterface2<U>', 'IInterface3<V>'. ";

        // Repeat the error message many times to reach approximately 50000 characters
        int targetLength = 50000;
        int currentLength = 0;
        int errorNumber = 1;

        while (currentLength < targetLength)
        {
            string fullError = $"[Error {errorNumber:D4}] {baseErrorMessage}{detailedError}";
            errorBuilder.AppendLine(fullError);
            currentLength = errorBuilder.Length;
            errorNumber++;
        }

        string largeErrorMessage = errorBuilder.ToString();

        // Verify the message is indeed large (around 50000 characters)
        Assert.Greater(largeErrorMessage.Length, 45000, "Error message should be at least 45000 characters");
        Assert.Less(largeErrorMessage.Length, 55000, "Error message should be less than 55000 characters");

        // This test should fail with a very large error message
        Assert.Fail(largeErrorMessage);
    }
}