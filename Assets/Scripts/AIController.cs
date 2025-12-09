// using UnityEngine;
// using TMPro;
// using System.Reflection;
// using System.Collections.Generic;

// public static class Functions
// {
//     public static string Forward()
//     {
//         return "Forward";
//     }
//     public static string Backward()
//     {
//         return "Backward";
//     }
//     public static string TurnLeft()
//     {
//         return "TurnLeft";
//     }
//     public static string TurnRight()
//     {
//         return "TurnRight";
//     }
// }
// public class AIController : MonoBehaviour
// {

//     public TMP_InputField inputField;
//     public SimpleCarController carController;
//     public LLMCharacter llmCharacter;

//     private void Start()
//     {
//         inputField.onSubmit.AddListener(onInputFieldSubmit);
//         // inputField.Select();
//         llmCharacter.grammarString = MultipleChoiceGrammar();

//         // // TMP_InputFieldのonEndEditイベントにリスナーを追加
//         // inputField.onEndEdit.AddListener(OnInputFieldEndEdit);
//     }

//     string[] GetFunctionNames()
//     {
//         List<string> functionNames = new List<string>();
//         foreach (var function in typeof(Functions).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)) functionNames.Add(function.Name);
//         return functionNames.ToArray();
//     }

//     string MultipleChoiceGrammar()
//     {
//         return "root ::= (\"" + string.Join("\" | \"", GetFunctionNames()) + "\")";
//     }
//     string ConstructPrompt(string message)
//     {
//         string prompt = "Which of the following choices matches best the input?\n\n";
//         prompt += "Input:" + message + "\n\n";
//         prompt += "Choices:\n";
//         foreach(string functionName in GetFunctionNames()) prompt += $"- {functionName}\n";
//         prompt += "\nAnswer directly with the choice";
//         return prompt;
//     }

//     string CallFunction(string functionName)
//     {
//         return (string) typeof(Functions).GetMethod(functionName).Invoke(null, null);
//     }

//     async void onInputFieldSubmit(string message)
//     {
//         inputField.interactable = false;
//         string functionName = await llmCharacter.Chat(ConstructPrompt(message));
//         string result = CallFunction(functionName);
//         Debug.Log($"Calling {functionName}\n{result}");
//         HandleLLMResponse(result);
//         inputField.interactable = true;
//     }

//     private void HandleLLMResponse(string response)
//     {
//         // Sanitize the response to make matching easier (lowercase, remove extra whitespace)
//         string command = response.ToLower().Trim();

//         // Control the car's movement based on the LLM's response.
//         // Checking for "right" and "left" first can help with more complex commands.
//         if (command.Contains("right"))
//         {
//             Debug.Log("Command recognized: Turn Right");
//             carController.TurnRight();
//         }
//         else if (command.Contains("left"))
//         {
//             Debug.Log("Command recognized: Turn Left");
//             carController.TurnLeft();
//         }
//         else if (command.Contains("forward") || command.Contains("go"))
//         {
//             Debug.Log("Command recognized: Forward");
//             carController.Forward();
//         }
//         else if (command.Contains("back") || command.Contains("reverse"))
//         {
//             Debug.Log("Command recognized: Backward");
//             carController.Backward();
//         }
//         else
//         {
//             Debug.Log("Invalid command received: " + response);
//             // For safety, stop the car if the command is not understood.
//         }
//     }
// }