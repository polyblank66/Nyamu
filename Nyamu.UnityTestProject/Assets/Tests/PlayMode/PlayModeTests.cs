using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Nyamu.Tests
{
    public class NyamuPlayModeTests
    {
        [UnityTest]
        public IEnumerator SimplePlayModeTest()
        {
            // Basic test
            yield return new WaitForSeconds(0.1f);

            // Basic assertion
            Assert.IsTrue(true, "This should always pass");

            // Verify Unity context is available
            Assert.IsNotNull(Application.dataPath, "Application.dataPath should be available");
        }
        
        [UnityTest]
        public IEnumerator GameObjectCreationTest()
        {
            // GameObject creation and test
            var gameObject = new GameObject("TestObject");

            yield return new WaitForSeconds(0.1f);

            Assert.IsNotNull(gameObject, "GameObject should be created");
            Assert.AreEqual("TestObject", gameObject.name, "GameObject name should match");

            // Cleanup
            Object.DestroyImmediate(gameObject);
        }
        
        [UnityTest]
        public IEnumerator ComponentTest()
        {
            // Component test
            var gameObject = new GameObject("TestObject");
            var rigidbody = gameObject.AddComponent<Rigidbody>();

            yield return new WaitForSeconds(0.1f);

            Assert.IsNotNull(rigidbody, "Rigidbody component should be added");
            Assert.AreEqual(1.0f, rigidbody.mass, "Default mass should be 1.0");

            // Cleanup
            Object.DestroyImmediate(gameObject);
        }
        
        [UnityTest]
        public IEnumerator FailingAssertionTest()
        {
            // Intentionally failing test
            yield return new WaitForSeconds(0.1f);

            Assert.IsTrue(false, "This test is designed to fail");
        }
        
        [UnityTest]
        public IEnumerator FailingEqualityTest()
        {
            // Equality test failure
            yield return new WaitForSeconds(0.1f);

            var expected = 10;
            var actual = 5;

            Assert.AreEqual(expected, actual, "Expected 10 but got 5");
        }
        
        [UnityTest]
        public IEnumerator FailingNullCheckTest()
        {
            // Null check test failure
            yield return new WaitForSeconds(0.1f);

            var nullObject = (GameObject)null;
            Assert.IsNotNull(nullObject, "This GameObject should not be null but it is");
        }
        
        [UnityTest]
        public IEnumerator FailingExceptionTest()
        {
            // Exception test failure
            yield return new WaitForSeconds(0.1f);

            throw new System.InvalidOperationException("This is an intentional test exception");
        }
    }
}