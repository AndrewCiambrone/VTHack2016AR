/*============================================================================== 
Copyright (c) 2015 PTC Inc. All Rights Reserved.

Copyright (c) 2012-2014 Qualcomm Connected Experiences, Inc. All Rights Reserved. 

Vuforia is a trademark of PTC Inc., registered in the United States and other 
countries.   
==============================================================================*/

using System.Collections.Generic;
using UnityEngine;
using Vuforia;

/// <summary>
/// A custom event handler for TextReco-events
/// </summary>
public class TextEventHandler : MonoBehaviour, ITextRecoEventHandler, IVideoBackgroundEventHandler
{
    #region PRIVATE_MEMBER_VARIABLES

    // Size of text search area in percentage of screen
    private float mLoupeWidth = 0.9f;
    private float mLoupeHeight = 0.5f;
    // Alpha value for area outside of text search
    private float mBackgroundAlpha = 0.7f;
    // Size of text box for visualizing detected words in percentage of remaining screen outside text search area
    private float mTextboxWidth = 0.9f;
    private float mTextboxHeight = 0.95f;
    // Number of words before scaling word list
    private int mFixedWordCount = 9;
    // Padding between lines in word list
    private float mWordPadding = 0.05f;
    // Minimum line height for word list
    private float mMinLineHeight = 15.0f;
    // Line width of viusalized boxes around detected words
    private float mBBoxLineWidth = 30.0f;
    // Padding between detected words and visualized boxes
    private float mBBoxPadding = 0.0f;
    // Color of visualized boxes around detected words
    private Color mBBoxColor = new Color(1.0f, 0.447f, 0.0f, 1.0f);

    private Rect mDetectionAndTrackingRect;
    private Texture2D mBackgroundTexture;
    private Texture2D mBoundingBoxTexture;
    private Material mBoundingBoxMaterial;

    private GUIStyle mWordStyle;
    private bool mIsTablet;
    private bool mIsInitialized;
    private bool mVideoBackgroundChanged;

    private readonly List<WordResult> mSortedWords = new List<WordResult>();

    [SerializeField] 
    private Material boundingBoxMaterial = null;
    #endregion

    #region UNTIY_MONOBEHAVIOUR_METHODS

    public void InitHandler()
    {
        // create the background texture (size 1x1, can be scaled to any size)
        mBackgroundTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        mBackgroundTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, mBackgroundAlpha));
        mBackgroundTexture.Apply(false);

        // create the texture for bounding boxes
        mBoundingBoxTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        mBoundingBoxTexture.SetPixel(0, 0, mBBoxColor);
        mBoundingBoxTexture.wrapMode = TextureWrapMode.Repeat; 
        mBoundingBoxTexture.Apply(false);

        mBoundingBoxMaterial = new Material(boundingBoxMaterial);
        mBoundingBoxMaterial.SetTexture("_MainTex", mBoundingBoxTexture);

        initDict();

        mWordStyle = new GUIStyle();
        mWordStyle.normal.textColor = Color.white;
        mWordStyle.alignment = TextAnchor.UpperCenter;
        mWordStyle.font = Resources.Load("SourceSansPro-Regular_big") as Font;

        mIsTablet = IsTablet();
        if (VuforiaRuntimeUtilities.IsPlayMode())
            mIsTablet = false;
        if (mIsTablet)
        {
            mLoupeWidth = 0.6f;
            mLoupeHeight = 0.1f;
            mTextboxWidth = 0.6f;
            mFixedWordCount = 14;
        }

        // register to TextReco events
        var trBehaviour = GetComponent<TextRecoBehaviour>();
        if (trBehaviour)
        {
            trBehaviour.RegisterTextRecoEventHandler(this);
        }

        // register for the OnVideoBackgroundConfigChanged event at the VuforiaBehaviour
        VuforiaBehaviour vuforiaBehaviour = (VuforiaBehaviour)FindObjectOfType(typeof(VuforiaBehaviour));
        if (vuforiaBehaviour)
        {
            vuforiaBehaviour.RegisterVideoBgEventHandler(this);
        }
    }

    public void Draw()
    {
        // draw background - tracking:
        DrawMaskedRectangle(mDetectionAndTrackingRect);
        DrawWordList();
    }

    void OnRenderObject()
    {
        DrawWordBoundingBoxes();
    }

    public void UpdateHandler()
    {
        // once the text tracker has initialized and every time the video background changed, set the region of interest
        if (mIsInitialized && mVideoBackgroundChanged)
        {
            TextTracker textTracker = TrackerManager.Instance.GetTracker<TextTracker>();
            if (textTracker != null)
            {
                CalculateLoupeRegion();
                textTracker.SetRegionOfInterest(mDetectionAndTrackingRect, mDetectionAndTrackingRect);
            }
            mVideoBackgroundChanged = false;
        }
    }
    
    #endregion // UNTIY_MONOBEHAVIOUR_METHODS



    #region ITextRecoEventHandler_IMPLEMENTATION

    /// <summary>
    /// Called when the text reco system has finished initializing
    /// </summary>
    public void OnInitialized()
    {
        CalculateLoupeRegion();
        mIsInitialized = true;
    }

    /// <summary>
    /// This method will be called whenever a new word has been detected
    /// </summary>
    /// <param name="wordResult">New trackable with current pose</param>
    public void OnWordDetected(WordResult wordResult)
    {
        var word = wordResult.Word;
        if (ContainsWord(word))
            Debug.LogError("Word was already detected before!");


        Debug.Log("Text: New word: " + wordResult.Word.StringValue + "(" + wordResult.Word.ID + ")");
        AddWord(wordResult);
    }

    /// <summary>
    /// This method will be called whenever a tracked word has been lost and is not tracked anymore
    /// </summary>
    public void OnWordLost(Word word)
    {
        if (!ContainsWord(word))
            Debug.LogError("Non-existing word was lost!");

        Debug.Log("Text: Lost word: " + word.StringValue + "(" + word.ID + ")");

        RemoveWord(word);
    }

    #endregion // PUBLIC_METHODS 


    
    #region IVideoBackgroundEventHandler_IMPLEMENTATION
    
    // set a flag that the video background has changed. This means the region of interest has to be set again.
    public void OnVideoBackgroundConfigChanged()
    {
        mVideoBackgroundChanged = true;
    }

    #endregion // IVideoBackgroundEventHandler_IMPLEMENTATION



    #region PRIVATE_METHODS

    /// <summary>
    /// Draw a 3d bounding box around each currently tracked word
    /// </summary>
    private void DrawWordBoundingBoxes()
    {
        // render a quad around each currently tracked word
        //foreach (var word in mSortedWords)
        //{
         //   var pos = word.Position;
          //  var orientation = word.Orientation;
          //  var size = word.Word.Size;
          //  var pose = Matrix4x4.TRS(pos, orientation, new Vector3(size.x, 1, size.y));

          //  var cornersObject = new[]
           //     {
          //          new Vector3(-0.5f, 0.0f, -0.5f), new Vector3(0.5f, 0.0f, -0.5f),
          //          new Vector3(0.5f, 0.0f, 0.5f), new Vector3(-0.5f, 0.0f, 0.5f)
          //      };
           // var corners = new Vector2[cornersObject.Length];
          //  for (int i = 0; i < cornersObject.Length; i++)
           //     corners[i] = Camera.current.WorldToScreenPoint(pose.MultiplyPoint(cornersObject[i]));
          //  DrawBoundingBox(corners);
       // }
    }

    /// <summary>
    /// Print string values for all currently tracked words.
    /// </summary>
    private void DrawWordList()
    {
        var sortedWords = mSortedWords;

        var textBoxWidth = Screen.width * mTextboxWidth;
        var textBoxHeight = (Screen.height - mDetectionAndTrackingRect.yMax) * mTextboxHeight;
        var textBoxOffsetLeft = (Screen.width - textBoxWidth) * 0.5f;
        var textBoxOffsetTop = mDetectionAndTrackingRect.yMax + (Screen.height - (textBoxHeight + mDetectionAndTrackingRect.yMax)) * 0.5f;

        var textBox = new Rect(textBoxOffsetLeft, textBoxOffsetTop, textBoxWidth, textBoxHeight);
        Rect wordBox;
        var scale = ComputeScaleForWordList(mSortedWords.Count, textBox, out wordBox);

        var oldMatrix = GUI.matrix;

        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(Screen.width * 0.5f, textBoxOffsetTop));

        wordBox.y += wordBox.height * mWordPadding;
        string words = "";
        foreach (var word in sortedWords)
        {
            //if ((wordBox.yMax - textBoxOffsetTop) * scale > textBox.height)
            //    break;

            //wordBox.x = Screen.width * 3 / 4;

            //wordBox.x = Screen.width / 4;
            //GUI.Label(wordBox, wordDict(word.Word.StringValue.ToLower()), mWordStyle);
            //wordBox.x = -1 * Screen.width / 6;
            //GUI.Label(wordBox, wordDict(word.Word.StringValue.ToLower()), mWordStyle);
            //GUI.Label(wordBox, word.Word.StringValue, mWordStyle);
            //wordBox.y += (wordBox.height + wordBox.height * mWordPadding);
            words = words + " " + wordDict(word.Word.StringValue.ToLower());
        }
        wordBox.x = Screen.width / 4;
        GUI.Label(wordBox, wrapString(words, Screen.width / 2), mWordStyle);
        GUI.matrix = oldMatrix;
    }

    string wrapString(string msg, int width)
    {
        string[] words = msg.Split(" "[0]);
        string retVal = ""; //returning string 
        string NLstr = "";  //leftover string on new line
        for (int index = 0; index < words.Length; index++)
        {
            string word = words[index].Trim();
            //if word exceeds width
            if (words[index].Length >= width + 2)
            {
                string[] temp = new string[5];
                int i = 0;
                while (words[index].Length > width)
                { //word exceeds width, cut it at widrh
                    temp[i] = words[index].Substring(0, width) + "\n"; //cut the word at width
                    words[index] = words[index].Substring(width);     //keep remaining word
                    i++;
                    if (words[index].Length <= width)
                    { //the balance is smaller than width
                        temp[i] = words[index];
                        NLstr = temp[i];
                    }
                }
                retVal += "\n";
                for (int x = 0; x < i + 1; x++)
                { //loops through temp array
                    retVal = retVal + temp[x];
                }
            }
            else if (index == 0)
            {
                retVal = words[0];
                NLstr = retVal;
            }
            else if (index > 0)
            {
                if (NLstr.Length + words[index].Length <= width)
                {
                    retVal = retVal + " " + words[index];
                    NLstr = NLstr + " " + words[index]; //add the current line length
                }
                else if (NLstr.Length + words[index].Length > width)
                {
                    retVal = retVal + "\n" + words[index];
                    NLstr = words[index]; //reset the line length
                    print("newline! at word " + words[index]);
                }
            }
        }
        return retVal;
    }

    private void DrawMaskedRectangle(Rect rectangle)
    {
        // draw four texture quads in UI that mask out the given region
        GUI.DrawTexture(new Rect(0f, 0f, rectangle.xMin, Screen.height), mBackgroundTexture);
        GUI.DrawTexture(new Rect(rectangle.xMin, 0f, rectangle.width, rectangle.yMin), mBackgroundTexture);
        GUI.DrawTexture(new Rect(rectangle.xMin, rectangle.yMax, rectangle.width, Screen.height - rectangle.yMax), mBackgroundTexture);
        GUI.DrawTexture(new Rect(rectangle.xMax, 0f, Screen.width - rectangle.xMax, Screen.height), mBackgroundTexture);
    }

    private void DrawBoundingBox(Vector2[] corners)
    {
        var normals = new Vector2[4];
        for (var i = 0; i < 4; i++)
        {
            var p0 = corners[i];
            var p1 = corners[(i + 1)%4];
            normals[i] = (p1 - p0).normalized;
            normals[i] = new Vector2(normals[i].y, -normals[i].x);
        }

        //add padding to inner corners
        corners = ExtendCorners(corners, normals, mBBoxPadding);
        //computer outer corners
        var outerCorners = ExtendCorners(corners, normals, mBBoxLineWidth);

        //create vertices in screen space
        var vertices = new Vector3[8];
        for (var i = 0; i < 4; i++)
        {
            vertices[i] = new Vector3(corners[i].x, corners[i].y, Camera.current.nearClipPlane);
            vertices[i + 4] = new Vector3(outerCorners[i].x, outerCorners[i].y, Camera.current.nearClipPlane);
        }
        //transform vertices into world space
        for (int i = 0; i < 8; i++)
            vertices[i] = Camera.current.ScreenToWorldPoint(vertices[i]);

        var mesh = new Mesh()
            {
                vertices = vertices,
                uv = new Vector2[8],
                triangles = new[]
                    {
                        0, 5, 4, 1, 5, 0,
                        1, 6, 5, 2, 6, 1,
                        2, 7, 6, 3, 7, 2,
                        3, 4, 7, 0, 4, 3
                    },
            };

        mBoundingBoxMaterial.SetPass(0);
        Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        //Graphics.DrawMesh(mesh, Matrix4x4.identity);
        Destroy(mesh);
    }


    private static Vector2[] ExtendCorners(Vector2[] corners, Vector2[] normals, float extension)
    {
        //compute positions along the outer side of the boundary
        var linePoints = new Vector2[corners.Length * 2];
        for (var i = 0; i < corners.Length; i++)
        {
            var p0 = corners[i];
            var p1 = corners[(i + 1) % 4];

            var po0 = p0 + normals[i] * extension;
            var po1 = p1 + normals[i] * extension;
            linePoints[i * 2] = po0;
            linePoints[i * 2 + 1] = po1;
        }

        //compute corners of outer side of bounding box lines
        var outerCorners = new Vector2[corners.Length];
        for (var i = 0; i < corners.Length; i++)
        {
            var i2 = i * 2;
            outerCorners[(i + 1) % 4] = IntersectLines(linePoints[i2], linePoints[i2 + 1], linePoints[(i2 + 2) % 8],
                                             linePoints[(i2 + 3) % 8]);
        }
        return outerCorners;
    }

    /// <summary>
    /// Intersect the line p1-p2 with the line p3-p4
    /// </summary>
    private static Vector2 IntersectLines(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
    {
        var denom = (p1.x - p2.x) * (p3.y - p4.y) - (p1.y - p2.y) * (p3.x - p4.x);
        var x = ((p1.x * p2.y - p1.y * p2.x) * (p3.x - p4.x) - (p1.x - p2.x) * (p3.x * p4.y - p3.y * p4.x)) / denom;
        var y = ((p1.x * p2.y - p1.y * p2.x) * (p3.y - p4.y) - (p1.y - p2.y) * (p3.x * p4.y - p3.y * p4.x)) / denom;
        return new Vector2(x, y);
    }


    private static bool IsTablet()
    {
#if (UNITY_IPHONE || UNITY_IOS)
  #if (UNITY_5_2_1 || UNITY_5_2_0 || UNITY_5_1 || UNITY_5_0) //Unity 5, up to 5.2.1
        return UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad1Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad2Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad3Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad4Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadMini1Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadMini2Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadMini3Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadAir1 ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadAir2 ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadUnknown;
  #elif (UNITY_5_3 || UNITY_5_2) //Unity 5.2.2 and above
        return UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad1Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad2Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad3Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPad4Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadMini1Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadMini2Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadMini3Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadMini4Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadPro1Gen ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadAir1 ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadAir2 ||
               UnityEngine.iOS.Device.generation == UnityEngine.iOS.DeviceGeneration.iPadUnknown;
  #elif (UNITY_4_6_9 || UNITY_4_6_10) //Unity 4.6.9 and above
        return iPhone.generation == iPhoneGeneration.iPad1Gen ||
               iPhone.generation == iPhoneGeneration.iPad2Gen ||
               iPhone.generation == iPhoneGeneration.iPad3Gen ||
               iPhone.generation == iPhoneGeneration.iPad4Gen ||
               iPhone.generation == iPhoneGeneration.iPadMini1Gen ||
               iPhone.generation == iPhoneGeneration.iPadMini2Gen ||
               iPhone.generation == iPhoneGeneration.iPadMini3Gen ||
               iPhone.generation == iPhoneGeneration.iPadAir2 ||
               iPhone.generation == iPhoneGeneration.iPad5Gen ||
               iPhone.generation == iPhoneGeneration.iPadMini4Gen ||
               iPhone.generation == iPhoneGeneration.iPadPro1Gen ||
               iPhone.generation == iPhoneGeneration.iPadUnknown;
  #else
        return iPhone.generation == iPhoneGeneration.iPad1Gen ||
               iPhone.generation == iPhoneGeneration.iPad2Gen ||
               iPhone.generation == iPhoneGeneration.iPad3Gen ||
               iPhone.generation == iPhoneGeneration.iPad4Gen ||
               iPhone.generation == iPhoneGeneration.iPadMini1Gen ||
               iPhone.generation == iPhoneGeneration.iPadMini2Gen ||
               iPhone.generation == iPhoneGeneration.iPadMini3Gen ||
               iPhone.generation == iPhoneGeneration.iPadAir2 ||
               iPhone.generation == iPhoneGeneration.iPadUnknown;
  #endif// endif UNITY_X_Y version check
#else
        var screenWidth = Screen.width / Screen.dpi;
        var screenHeight = Screen.height / Screen.dpi;
        var diagonal = Mathf.Sqrt(Mathf.Pow(screenWidth, 2) + Mathf.Pow(screenHeight, 2));
        //tablets usually have a screen size greater than 6 inches
        return diagonal >= 6;
#endif

    }

    /// <summary>
    /// compute the scale for printing the string values of the currently tracked words
    /// </summary>
    /// <param name="numWords">Number of currently tracked words</param>
    /// <param name="totalArea">Region where all words should be printed</param>
    /// <param name="firstWord">Region for first word</param>
    /// <returns>necessary scale to put all words into the area</returns>
    private float ComputeScaleForWordList(int numWords, Rect totalArea, out Rect firstWord)
    {
        if (numWords < mFixedWordCount)
            numWords = mFixedWordCount;
        
        
        var originalHeight = mWordStyle.lineHeight;
        var requestedHeight = totalArea.height / (numWords + mWordPadding * (numWords + 1));

        if (requestedHeight < mMinLineHeight)
        {
            requestedHeight = mMinLineHeight;
        }
        var scale = requestedHeight / originalHeight;

        var newWidth = totalArea.width / scale;
        firstWord = new Rect(totalArea.xMin + (totalArea.width - newWidth) * 0.5f, totalArea.yMin, newWidth, originalHeight);
        return scale;
    }


    private void AddWord(WordResult wordResult)
    {
        //add new word into sorted list
        var cmp = new ObbComparison();
        int i = 0;
        while (i < mSortedWords.Count && cmp.Compare(mSortedWords[i], wordResult) < 0)
        {
            i++;
        }

        if (i < mSortedWords.Count)
        {
            mSortedWords.Insert(i, wordResult);
        }
        else
        {
            mSortedWords.Add(wordResult);
        }
    }

    private void RemoveWord(Word word)
    {
        for (int i = 0; i < mSortedWords.Count; i++)
        {
            if (mSortedWords[i].Word.ID == word.ID)
            {
                mSortedWords.RemoveAt(i);
                break;
            }
        }
    }

    private bool ContainsWord(Word word)
    {
        foreach (var w in mSortedWords)
            if (w.Word.ID == word.ID)
                return true;
        return false;
    }

    private void CalculateLoupeRegion()
    {
        // define area for text search
        var loupeWidth = mLoupeWidth * Screen.width;
        var loupeHeight = mLoupeHeight * Screen.height;
        var leftOffset = (Screen.width - loupeWidth) * 0.5f;
        var topOffset = leftOffset;
        mDetectionAndTrackingRect = new Rect(leftOffset, topOffset, loupeWidth, loupeHeight);
    }

    Dictionary<string, string> words;

    private void initDict()
    {
        words = new Dictionary<string, string>()
        {
            {"a", "un"},
            {"able", "poder"},
            {"about", "acerca de"},
            {"above", "encima"},
            {"accept", "aceptar"},
            {"across", "a través de"},
            {"act", "acto"},
            {"actually", "realmente"},
            {"add", "añadir"},
            {"admit", "admitir"},
            {"afraid", "asustado"},
            {"after", "después"},
            {"afternoon", "tarde"},
            {"again", "de nuevo"},
            {"against", "en contra"},
            {"age", "años"},
            {"ago", "hace"},
            {"agree", "de acuerdo"},
            {"ah", "ah"},
            {"ahead", "adelante"},
            {"air", "aire"},
            {"all", "todas"},
            {"allow", "permitir"},
            {"almost", "casi"},
            {"alone", "solo"},
            {"along", "a lo largo"},
            {"already", "ya"},
            {"alright", "bien"},
            {"also", "además"},
            {"although", "a pesar de que"},
            {"always", "siempre"},
            {"am", "a.m"},
            {"amaze", "asombro"},
            {"an", "un"},
            {"and", "y"},
            {"anger", "enfado"},
            {"angry", "enojado"},
            {"animal", "animal"},
            {"annoy", "molestar"},
            {"another", "otro"},
            {"answer", "responder"},
            {"any", "alguna"},
            {"anymore", "nunca más"},
            {"anyone", "nadie"},
            {"anything", "cualquier cosa"},
            {"anyway", "de todas formas"},
            {"apartment", "apartamento"},
            {"apparently", "aparentemente"},
            {"appear", "aparecer"},
            {"approach", "enfoque"},
            {"are", "son"},
            {"area", "zona"},
            {"aren't", "no son"},
            {"arm", "brazo"},
            {"around", "alrededor"},
            {"arrive", "llegar"},
            {"as", "como"},
            {"ask", "pedir"},
            {"asleep", "dormido"},
            {"ass", "culo"},
            {"at", "a"},
            {"attack", "ataque"},
            {"attempt", "intento"},
            {"attention", "atención"},
            {"aunt", "tía"},
            {"avoid", "evitar"},
            {"away", "lejos"},
            {"baby", "bebé"},
            {"back", "espalda"},
            {"bad", "malo"},
            {"bag", "bolso"},
            {"ball", "pelota"},
            {"band", "banda"},
            {"bar", "bar"},
            {"barely", "apenas"},
            {"bathroom", "baño"},
            {"be", "ser"},
            {"beat", "golpear"},
            {"beautiful", "hermosa"},
            {"became", "convirtió"},
            {"because", "porque"},
            {"become", "volverse"},
            {"bed", "cama"},
            {"bedroom", "Cuarto"},
            {"been", "estado"},
            {"before", "antes de"},
            {"began", "empezó"},
            {"begin", "empezar"},
            {"behind", "detrás"},
            {"believe", "creer"},
            {"bell", "campana"},
            {"beside", "junto a"},
            {"besides", "además"},
            {"best", "mejor"},
            {"better", "mejor"},
            {"between", "Entre"},
            {"big", "grande"},
            {"bit", "poco"},
            {"bite", "mordedura"},
            {"black", "negro"},
            {"blink", "parpadeo"},
            {"block", "bloquear"},
            {"blonde", "rubia"},
            {"blood", "sangre"},
            {"blue", "azul"},
            {"blush", "sonrojo"},
            {"body", "cuerpo"},
            {"book", "libro"},
            {"bore", "aburrir"},
            {"both", "ambos"},
            {"bother", "molestia"},
            {"bottle", "botella"},
            {"bottom", "fondo"},
            {"box", "caja"},
            {"boy", "chico"},
            {"boyfriend", "novio"},
            {"brain", "cerebro"},
            {"break", "romper"},
            {"breakfast", "desayuno"},
            {"breath", "aliento"},
            {"breathe", "respirar"},
            {"bright", "brillante"},
            {"bring", "traer"},
            {"broke", "rompió"},
            {"broken", "roto"},
            {"brother", "hermano"},
            {"brought", "trajo"},
            {"brown", "marrón"},
            {"brush", "cepillo"},
            {"build", "construir"},
            {"burn", "quemar"},
            {"burst", "ráfaga"},
            {"bus", "autobús"},
            {"business", "negocio"},
            {"busy", "ocupado"},
            {"but", "pero"},
            {"buy", "comprar"},
            {"by", "por"},
            {"call", "llamada"},
            {"calm", "calma"},
            {"came", "vino"},
            {"can", "poder"},
            {"can't", "hipocresía"},
            {"car", "coche"},
            {"card", "tarjeta"},
            {"care", "cuidado"},
            {"carefully", "cuidadosamente"},
            {"carry", "llevar"},
            {"case", "caso"},
            {"cat", "gato"},
            {"catch", "captura"},
            {"caught", "atrapado"},
            {"cause", "porque"},
            {"cell", "celda"},
            {"chair", "silla"},
            {"chance", "oportunidad"},
            {"change", "cambio"},
            {"chase", "persecución"},
            {"check", "comprobar"},
            {"cheek", "mejilla"},
            {"chest", "pecho"},
            {"child", "niño"},
            {"children", "niños"},
            {"chuckle", "risita"},
            {"city", "ciudad"},
            {"class", "clase"},
            {"clean", "limpiar"},
            {"clear", "claro"},
            {"climb", "escalada"},
            {"close", "cerca"},
            {"clothes", "ropa"},
            {"coffee", "café"},
            {"cold", "frío"},
            {"college", "Universidad"},
            {"color", "color"},
            {"come", "ven"},
            {"comment", "comentario"},
            {"complete", "completar"},
            {"completely", "completamente"},
            {"computer", "computadora"},
            {"concern", "preocupación"},
            {"confuse", "confundir"},
            {"consider", "considerar"},
            {"continue", "continuar"},
            {"control", "controlar"},
            {"conversation", "conversación"},
            {"cool", "guay"},
            {"corner", "esquina"},
            {"couch", "sofá"},
            {"could", "podría"},
            {"couldn't", "no podía"},
            {"counter", "mostrador"},
            {"couple", "par"},
            {"course", "curso"},
            {"cover", "cubrir"},
            {"crack", "grieta"},
            {"crazy", "loca"},
            {"cross", "cruzar"},
            {"crowd", "multitud"},
            {"cry", "llorar"},
            {"cup", "taza"},
            {"cut", "cortar"},
            {"cute", "linda"},
            {"dad", "papá"},
            {"damn", "Maldita sea"},
            {"dance", "baile"},
            {"dark", "oscuro"},
            {"date", "fecha"},
            {"daughter", "hija"},
            {"day", "día"},
            {"dead", "muerto"},
            {"deal", "acuerdo"},
            {"dear", "querido"},
            {"death", "muerte"},
            {"decide", "decidir"},
            {"deep", "profundo"},
            {"definitely", "seguro"},
            {"desk", "escritorio"},
            {"did", "hizo"},
            {"didn't", "no lo hizo"},
            {"die", "morir"},
            {"different", "diferente"},
            {"dinner", "cena"},
            {"direction", "dirección"},
            {"disappear", "desaparecer"},
            {"do", "hacer"},
            {"doctor", "doctor"},
            {"does", "hace"},
            {"doesn't", "no hace"},
            {"dog", "perro"},
            {"don't", "no hacer"},
            {"done", "hecho"},
            {"door", "puerta"},
            {"doubt", "duda"},
            {"down", "abajo"},
            {"drag", "arrastrar"},
            {"draw", "dibujar"},
            {"dream", "sueña"},
            {"dress", "vestir"},
            {"drink", "beber"},
            {"drive", "manejar"},
            {"drop", "soltar"},
            {"drove", "condujo"},
            {"dry", "seco"},
            {"during", "durante"},
            {"each", "cada"},
            {"ear", "oreja"},
            {"early", "temprano"},
            {"easily", "fácilmente"},
            {"easy", "fácil"},
            {"eat", "comer"},
            {"edge", "borde"},
            {"either", "ya sea"},
            {"else", "más"},
            {"empty", "vacío"},
            {"end", "final"},
            {"enjoy", "disfrutar"},
            {"enough", "suficiente"},
            {"enter", "entrar"},
            {"entire", "todo"},
            {"escape", "escapar"},
            {"especially", "especialmente"},
            {"even", "incluso"},
            {"evening", "noche"},
            {"eventually", "finalmente"},
            {"ever", "nunca"},
            {"every", "cada"},
            {"everyone", "todo el mundo"},
            {"everything", "todo"},
            {"exactly", "exactamente"},
            {"except", "excepto"},
            {"excite", "excitar"},
            {"exclaim", "exclamar"},
            {"excuse", "excusa"},
            {"expect", "esperar"},
            {"explain", "explique"},
            {"expression", "expresión"},
            {"eye", "ojo"},
            {"eyebrow", "ceja"},
            {"face", "cara"},
            {"fact", "hecho"},
            {"fall", "otoño"},
            {"family", "familia"},
            {"far", "lejos"},
            {"fast", "ayunar"},
            {"father", "padre"},
            {"fault", "culpa"},
            {"favorite", "favorito"},
            {"fear", "miedo"},
            {"feel", "sensación"},
            {"feet", "pies"},
            {"fell", "cayó"},
            {"felt", "sintió"},
            {"few", "pocos"},
            {"field", "campo"},
            {"fight", "lucha"},
            {"figure", "figura"},
            {"fill", "llenar"},
            {"finally", "finalmente"},
            {"find", "encontrar"},
            {"fine", "multa"},
            {"finger", "dedo"},
            {"finish", "acabado"},
            {"fire", "fuego"},
            {"first", "primero"},
            {"fit", "ajuste"},
            {"five", "cinco"},
            {"fix", "fijar"},
            {"flash", "destello"},
            {"flip", "dar la vuelta"},
            {"floor", "piso"},
            {"fly", "mosca"},
            {"focus", "atención"},
            {"follow", "seguir"},
            {"food", "comida"},
            {"foot", "pie"},
            {"for", "para"},
            {"force", "fuerza"},
            {"forget", "olvidar"},
            {"form", "formar"},
            {"forward", "adelante"},
            {"found", "encontró"},
            {"four", "las cuatro"},
            {"free", "gratis"},
            {"friend", "amigo"},
            {"from", "de"},
            {"front", "frente"},
            {"frown", "fruncir el ceño"},
            {"fuck", "Mierda"},
            {"full", "lleno"},
            {"fun", "divertido"},
            {"funny", "gracioso"},
            {"further", "promover"},
            {"game", "juego"},
            {"gasp", "jadear"},
            {"gave", "dio"},
            {"gaze", "mirada"},
            {"gently", "suavemente"},
            {"get", "obtener"},
            {"giggle", "risilla"},
            {"girl", "muchacha"},
            {"girlfriend", "Novia"},
            {"give", "dar"},
            {"given", "dado"},
            {"glad", "alegre"},
            {"glance", "vistazo"},
            {"glare", "deslumbramiento"},
            {"glass", "vaso"},
            {"go", "ir"},
            {"God", "Dios"},
            {"gone", "ido"},
            {"gonna", "va"},
            {"good", "bueno"},
            {"got", "tiene"},
            {"gotten", "conseguido"},
            {"grab", "agarrar"},
            {"great", "estupendo"},
            {"green", "verde"},
            {"greet", "saludar"},
            {"grey", "gris"},
            {"grin", "mueca"},
            {"grip", "apretón"},
            {"groan", "gemido"},
            {"ground", "suelo"},
            {"group", "grupo"},
            {"grow", "crecer"},
            {"guard", "Guardia"},
            {"guess", "adivinar"},
            {"gun", "pistola"},
            {"guy", "chico"},
            {"had", "tenido"},
            {"hadn't", "no tenía"},
            {"hair", "cabello"},
            {"half", "mitad"},
            {"hall", "sala"},
            {"hallway", "pasillo"},
            {"hand", "mano"},
            {"handle", "encargarse de"},
            {"hang", "colgar"},
            {"happen", "ocurrir"},
            {"happy", "contento"},
            {"hard", "difícil"},
            {"has", "tiene"},
            {"hate", "odio"},
            {"have", "tener"},
            {"haven't", "no tiene"},
            {"he", "él"},
            {"he'd", "él había"},
            {"he's", "él es"},
            {"head", "cabeza"},
            {"hear", "oír"},
            {"heard", "oído"},
            {"heart", "corazón"},
            {"heavy", "pesado"},
            {"held", "retenida"},
            {"hell", "infierno"},
            {"hello", "Hola"},
            {"help", "ayuda"},
            {"her", "su"},
            {"here", "aquí"},
            {"herself", "sí misma"},
            {"hey", "Oye"},
            {"hi", "Hola"},
            {"hide", "esconder"},
            {"high", "alto"},
            {"him", "él"},
            {"himself", "él mismo"},
            {"his", "su"},
            {"hit", "golpear"},
            {"hold", "sostener"},
            {"home", "hogar"},
            {"hope", "esperanza"},
            {"horse", "caballo"},
            {"hospital", "hospital"},
            {"hot", "caliente"},
            {"hour", "hora"},
            {"house", "casa"},
            {"how", "cómo"},
            {"however", "sin embargo"},
            {"hug", "abrazo"},
            {"huge", "enorme"},
            {"huh", "eh"},
            {"human", "humano"},
            {"hundred", "cien"},
            {"hung", "colgado"},
            {"hurry", "prisa"},
            {"hurt", "herir"},
            {"I", "yo"},
            {"I'd", "Carné de identidad"},
            {"I'll", "Enfermo"},
            {"I'm", "estoy"},
            {"I've", "He"},
            {"ice", "hielo"},
            {"idea", "idea"},
            {"if", "si"},
            {"ignore", "ignorar"},
            {"imagine", "imagina"},
            {"immediately", "inmediatamente"},
            {"important", "importante"},
            {"in", "en"},
            {"inside", "dentro"},
            {"instead", "en lugar"},
            {"interest", "interesar"},
            {"interrupt", "interrumpir"},
            {"into", "dentro"},
            {"is", "es"},
            {"isn't", "no es"},
            {"it", "eso"},
            {"it's", "sus"},
            {"its", "sus"},
            {"jacket", "chaqueta"},
            {"jeans", "pantalones"},
            {"jerk", "imbécil"},
            {"job", "trabajo"},
            {"join", "unirse"},
            {"joke", "broma"},
            {"jump", "saltar"},
            {"just", "sólo"},
            {"keep", "guardar"},
            {"kept", "mantenido"},
            {"key", "llave"},
            {"kick", "patada"},
            {"kid", "niño"},
            {"kill", "matar"},
            {"kind", "tipo"},
            {"kiss", "Beso"},
            {"kitchen", "cocina"},
            {"knee", "rodilla"},
            {"knew", "sabía"},
            {"knock", "golpe"},
            {"know", "saber"},
            {"known", "conocido"},
            {"lady", "dama"},
            {"land", "tierra"},
            {"large", "gran"},
            {"last", "último"},
            {"late", "tarde"},
            {"laugh", "risa"},
            {"lay", "laico"},
            {"lead", "dirigir"},
            {"lean", "apoyarse"},
            {"learn", "aprender"},
            {"least", "menos"},
            {"leave", "salir"},
            {"led", "LED"},
            {"left", "izquierda"},
            {"leg", "pierna"},
            {"less", "Menos"},
            {"let", "dejar"},
            {"letter", "carta"},
            {"lie", "mentira"},
            {"life", "vida"},
            {"lift", "ascensor"},
            {"light", "ligero"},
            {"like", "me gusta"},
            {"line", "línea"},
            {"lip", "labio"},
            {"listen", "escucha"},
            {"little", "pequeño"},
            {"live", "vivir"},
            {"lock", "bloquear"},
            {"locker", "armario"},
            {"long", "largo"},
            {"look", "Mira"},
            {"lose", "perder"},
            {"lost", "perdió"},
            {"lot", "mucho"},
            {"loud", "ruidoso"},
            {"love", "amor"},
            {"low", "bajo"},
            {"lunch", "almuerzo"},
            {"mad", "enojado"},
            {"made", "hecho"},
            {"make", "hacer"},
            {"man", "hombre"},
            {"manage", "gestionar"},
            {"many", "muchos"},
            {"mark", "marca"},
            {"marry", "casarse"},
            {"match", "partido"},
            {"matter", "importar"},
            {"may", "mayo"},
            {"maybe", "tal vez"},
            {"me", "yo"},
            {"mean", "media"},
            {"meant", "significaba"},
            {"meet", "reunirse"},
            {"memory", "memoria"},
            {"men", "hombres"},
            {"mention", "mención"},
            {"met", "reunió"},
            {"middle", "medio"},
            {"might", "podría"},
            {"mind", "mente"},
            {"mine", "mía"},
            {"minute", "minuto"},
            {"mirror", "espejo"},
            {"miss", "perder"},
            {"mom", "mamá"},
            {"moment", "momento"},
            {"money", "dinero"},
            {"month", "mes"},
            {"mood", "estado animico"},
            {"more", "Más"},
            {"morning", "Mañana"},
            {"most", "más"},
            {"mother", "madre"},
            {"mouth", "boca"},
            {"move", "movimiento"},
            {"movie", "película"},
            {"Mr.", "señor."},
            {"Mrs.", "Señora."},
            {"much", "mucho"},
            {"mum", "mamá"},
            {"mumble", "mascullar"},
            {"music", "música"},
            {"must", "debe"},
            {"mutter", "murmurar"},
            {"my", "mi"},
            {"myself", "mí mismo"},
            {"name", "nombre"},
            {"near", "cerca"},
            {"nearly", "casi"},
            {"neck", "cuello"},
            {"need", "necesitar"},
            {"nervous", "nervioso"},
            {"never", "Nunca"},
            {"new", "nuevo"},
            {"next", "siguiente"},
            {"nice", "agradable"},
            {"night", "noche"},
            {"no", "no"},
            {"nod", "asentir"},
            {"noise", "ruido"},
            {"none", "ninguna"},
            {"normal", "normal"},
            {"nose", "nariz"},
            {"not", "no"},
            {"note", "Nota"},
            {"nothing", "nada"},
            {"notice", "darse cuenta"},
            {"now", "ahora"},
            {"number", "número"},
            {"obviously", "obviamente"},
            {"of", "de"},
            {"off", "apagado"},
            {"offer", "oferta"},
            {"office", "oficina"},
            {"often", "a menudo"},
            {"oh", "Oh"},
            {"okay", "bueno"},
            {"old", "antiguo"},
            {"on", "en"},
            {"once", "una vez"},
            {"one", "uno"},
            {"only", "solamente"},
            {"onto", "sobre"},
            {"open", "abierto"},
            {"or", "o"},
            {"order", "orden"},
            {"other", "otro"},
            {"our", "nuestra"},
            {"out", "fuera"},
            {"outside", "fuera de"},
            {"over", "encima"},
            {"own", "propio"},
            {"pack", "paquete"},
            {"pain", "dolor"},
            {"paint", "pintura"},
            {"pair", "par"},
            {"pants", "pantalones"},
            {"paper", "papel"},
            {"parents", "padres"},
            {"park", "parque"},
            {"part", "parte"},
            {"party", "fiesta"},
            {"pass", "pasar"},
            {"past", "pasado"},
            {"pause", "pausa"},
            {"pay", "paga"},
            {"people", "gente"},
            {"perfect", "Perfecto"},
            {"perhaps", "quizás"},
            {"person", "persona"},
            {"phone", "teléfono"},
            {"pick", "recoger"},
            {"picture", "imagen"},
            {"piece", "pieza"},
            {"pink", "rosado"},
            {"piss", "mear"},
            {"place", "lugar"},
            {"plan", "plan"},
            {"play", "jugar"},
            {"please", "Por favor"},
            {"pocket", "bolsillo"},
            {"point", "punto"},
            {"police", "policía"},
            {"pop", "popular"},
            {"position", "posición"},
            {"possible", "posible"},
            {"power", "poder"},
            {"practically", "prácticamente"},
            {"present", "presente"},
            {"press", "prensa"},
            {"pretend", "fingir"},
            {"pretty", "bonita"},
            {"probably", "probablemente"},
            {"problem", "problema"},
            {"promise", "promesa"},
            {"pull", "Halar"},
            {"punch", "puñetazo"},
            {"push", "empujar"},
            {"put", "poner"},
            {"question", "pregunta"},
            {"quick", "rápido"},
            {"quickly", "con rapidez"},
            {"quiet", "tranquilo"},
            {"quietly", "tranquilamente"},
            {"quite", "bastante"},
            {"race", "carrera"},
            {"rain", "lluvia"},
            {"raise", "aumento"},
            {"ran", "corrió"},
            {"rang", "sonó"},
            {"rather", "más bien"},
            {"reach", "alcanzar"},
            {"read", "leer"},
            {"ready", "Listo"},
            {"real", "real"},
            {"realize", "darse cuenta de"},
            {"really", "De Verdad"},
            {"reason", "razón"},
            {"recognize", "reconocer"},
            {"red", "rojo"},
            {"relationship", "relación"},
            {"relax", "relajarse"},
            {"remain", "permanecer"},
            {"remember", "recuerda"},
            {"remind", "recordar"},
            {"repeat", "repetir"},
            {"reply", "respuesta"},
            {"respond", "responder"},
            {"rest", "descanso"},
            {"return", "regreso"},
            {"ride", "paseo"},
            {"right", "derecho"},
            {"ring", "anillo"},
            {"road", "la carretera"},
            {"rock", "rock"},
            {"roll", "rodar"},
            {"room", "habitación"},
            {"rose", "Rosa"},
            {"round", "redondo"},
            {"rub", "frotar"},
            {"run", "correr"},
            {"rush", "prisa"},
            {"sad", "triste"},
            {"safe", "seguro"},
            {"said", "dijo"},
            {"same", "mismo"},
            {"sat", "sábado"},
            {"save", "salvar"},
            {"saw", "Sierra"},
            {"say", "decir"},
            {"scare", "susto"},
            {"school", "colegio"},
            {"scream", "gritar"},
            {"search", "buscar"},
            {"seat", "asiento"},
            {"second", "segundo"},
            {"see", "ver"},
            {"seem", "parecer"},
            {"seen", "visto"},
            {"self", "yo"},
            {"send", "enviar"},
            {"sense", "sentido"},
            {"sent", "expedido"},
            {"serious", "grave"},
            {"seriously", "seriamente"},
            {"set", "conjunto"},
            {"settle", "resolver"},
            {"seven", "siete"},
            {"several", "varios"},
            {"shadow", "sombra"},
            {"shake", "sacudir"},
            {"share", "compartir"},
            {"she", "ella"},
            {"she'd", "cobertizo"},
            {"she's", "ella es"},
            {"shift", "cambio"},
            {"shirt", "camisa"},
            {"shit", "mierda"},
            {"shock", "choque"},
            {"shoe", "zapato"},
            {"shook", "sacudió"},
            {"shop", "tienda"},
            {"short", "corto"},
            {"shot", "Disparo"},
            {"should", "debería"},
            {"shoulder", "hombro"},
            {"shouldn't", "no debería"},
            {"shout", "gritar"},
            {"shove", "empujón"},
            {"show", "espectáculo"},
            {"shower", "ducha"},
            {"shrug", "encogimiento de hombros"},
            {"shut", "cerrar"},
            {"sick", "enfermos"},
            {"side", "lado"},
            {"sigh", "suspiro"},
            {"sight", "visión"},
            {"sign", "firmar"},
            {"silence", "silencio"},
            {"silent", "silencio"},
            {"simply", "simplemente"},
            {"since", "ya que"},
            {"single", "soltero"},
            {"sir", "señor"},
            {"sister", "hermana"},
            {"sit", "sentar"},
            {"situation", "situación"},
            {"six", "seis"},
            {"skin", "piel"},
            {"sky", "cielo"},
            {"slam", "golpe"},
            {"sleep", "dormir"},
            {"slightly", "ligeramente"},
            {"slip", "resbalón"},
            {"slow", "lento"},
            {"slowly", "despacio"},
            {"small", "pequeña"},
            {"smell", "olor"},
            {"smile", "sonreír"},
            {"smirk", "sonrisa afectada"},
            {"smoke", "fumar"},
            {"snap", "chasquido"},
            {"so", "asi que"},
            {"soft", "suave"},
            {"softly", "suavemente"},
            {"some", "algunos"},
            {"somehow", "de algun modo"},
            {"someone", "alguien"},
            {"something", "alguna cosa"},
            {"sometimes", "a veces"},
            {"somewhere", "algun lado"},
            {"son", "hijo"},
            {"song", "canción"},
            {"soon", "pronto"},
            {"sorry", "lo siento"},
            {"sort", "ordenar"},
            {"sound", "sonar"},
            {"space", "espacio"},
            {"speak", "hablar"},
            {"spend", "gastar"},
            {"spent", "gastado"},
            {"spoke", "habló"},
            {"spot", "lugar"},
            {"stair", "escalera"},
            {"stand", "estar"},
            {"star", "estrella"},
            {"stare", "mirar fijamente"},
            {"start", "comienzo"},
            {"state", "estado"},
            {"stay", "permanecer"},
            {"step", "paso"},
            {"stick", "palo"},
            {"still", "todavía"},
            {"stomach", "estómago"},
            {"stood", "destacado"},
            {"stop", "detener"},
            {"store", "almacenar"},
            {"story", "historia"},
            {"straight", "Derecho"},
            {"strange", "extraño"},
            {"street", "calle"},
            {"strong", "fuerte"},
            {"struggle", "lucha"},
            {"stuck", "atascado"},
            {"student", "estudiante"},
            {"study", "estudiar"},
            {"stuff", "cosas"},
            {"stupid", "estúpido"},
            {"such", "tal"},
            {"suck", "chupar"},
            {"sudden", "repentino"},
            {"suddenly", "repentinamente"},
            {"suggest", "sugerir"},
            {"summer", "el verano"},
            {"sun", "sol"},
            {"suppose", "suponer"},
            {"sure", "Por supuesto"},
            {"surprise", "sorpresa"},
            {"surround", "rodear"},
            {"sweet", "dulce"},
            {"table", "mesa"},
            {"take", "tomar"},
            {"taken", "tomado"},
            {"talk", "hablar"},
            {"tall", "alto"},
            {"teacher", "profesor"},
            {"team", "equipo"},
            {"tear", "lágrima"},
            {"teeth", "dientes"},
            {"tell", "contar"},
            {"ten", "diez"},
            {"than", "que"},
            {"thank", "gracias"},
            {"that", "ese"},
            {"that's", "eso es"},
            {"the", "el"},
            {"their", "su"},
            {"them", "ellos"},
            {"themselves", "sí mismos"},
            {"then", "luego"},
            {"there", "ahí"},
            {"there's", "hay"},
            {"these", "estas"},
            {"they", "ellos"},
            {"they'd", "Habían"},
            {"they're", "ellos son"},
            {"thick", "grueso"},
            {"thing", "cosa"},
            {"think", "pensar"},
            {"third", "tercero"},
            {"this", "esta"},
            {"those", "aquellos"},
            {"though", "aunque"},
            {"thought", "pensamiento"},
            {"three", "Tres"},
            {"threw", "arrojó"},
            {"throat", "garganta"},
            {"through", "mediante"},
            {"throw", "lanzar"},
            {"tie", "Corbata"},
            {"tight", "ajustado"},
            {"time", "hora"},
            {"tiny", "minúsculo"},
            {"tire", "neumático"},
            {"to", "a"},
            {"today", "hoy"},
            {"together", "juntos"},
            {"told", "dicho"},
            {"tomorrow", "mañana"},
            {"tone", "tono"},
            {"tongue", "lengua"},
            {"tonight", "esta noche"},
            {"too", "también"},
            {"took", "tomó"},
            {"top", "parte superior"},
            {"totally", "totalmente"},
            {"touch", "toque"},
            {"toward", "hacia"},
            {"town", "pueblo"},
            {"track", "pista"},
            {"trail", "sendero"},
            {"train", "tren"},
            {"tree", "árbol"},
            {"trip", "viaje"},
            {"trouble", "problema"},
            {"trust", "confianza"},
            {"truth", "verdad"},
            {"try", "tratar"},
            {"turn", "giro"},
            {"TV", "televisión"},
            {"twenty", "veinte"},
            {"two", "dos"},
            {"type", "tipo"},
            {"uncle", "tío"},
            {"under", "debajo"},
            {"understand", "entender"},
            {"until", "hasta"},
            {"up", "arriba"},
            {"upon", "sobre"},
            {"us", "nos"},
            {"use", "utilizar"},
            {"usual", "usual"},
            {"usually", "generalmente"},
            {"very", "muy"},
            {"visit", "visitar"},
            {"voice", "voz"},
            {"wait", "Espere"},
            {"wake", "despertar"},
            {"walk", "caminar"},
            {"wall", "pared"},
            {"want", "querer"},
            {"warm", "calentar"},
            {"warn", "advertir"},
            {"was", "estaba"},
            {"wasn't", "no era"},
            {"watch", "reloj"},
            {"water", "agua"},
            {"wave", "ola"},
            {"way", "camino"},
            {"we", "nosotros"},
            {"we'll", "bien"},
            {"we're", "fueron"},
            {"we've", "nos hemos"},
            {"wear", "vestir"},
            {"week", "semana"},
            {"weird", "extraños"},
            {"well", "bien"},
            {"went", "fuimos"},
            {"were", "fueron"},
            {"weren't", "no eran"},
            {"wet", "mojado"},
            {"what", "qué"},
            {"what's", "lo que es"},
            {"whatever", "lo que sea"},
            {"when", "cuando"},
            {"where", "dónde"},
            {"whether", "si"},
            {"which", "cual"},
            {"while", "mientras"},
            {"whisper", "susurro"},
            {"white", "blanco"},
            {"who", "quien"},
            {"whole", "todo"},
            {"why", "por qué"},
            {"wide", "amplio"},
            {"wife", "esposa"},
            {"will", "será"},
            {"wind", "viento"},
            {"window", "ventana"},
            {"wipe", "limpiar"},
            {"wish", "deseo"},
            {"with", "con"},
            {"within", "dentro"},
            {"without", "sin"},
            {"woke", "despertó"},
            {"woman", "mujer"},
            {"women", "mujer"},
            {"won't", "costumbre"},
            {"wonder", "preguntarse"},
            {"wood", "madera"},
            {"word", "palabra"},
            {"wore", "llevaba"},
            {"work", "trabajo"},
            {"world", "mundo"},
            {"worry", "preocupación"},
            {"worse", "peor"},
            {"would", "haría"},
            {"wouldn't", "no lo haría"},
            {"wow", "Guau"},
            {"wrap", "envolver"},
            {"write", "escribir"},
            {"wrong", "incorrecto"},
            {"yeah", "sí"},
            {"year", "año"},
            {"yell", "grito"},
            {"yes", "sí"},
            {"yet", "todavía"},
            {"you", "tú"},
            {"you'd", "Más te"},
            {"you'll", "Así,"},
            {"you're", "estás"},
            {"you've", "tienes"},
            {"young", "joven"},
            {"your", "tu"},
            {"yourself", "tú mismo"}
        };
        /*
        {
            {"new",  "Nueva"},
            {"york", "York" },
            {"art", "Arte" },
            {"museum",  "MUSEO"},
            {"bathroom", "BAÑO" },
            {"friend", "AMIGO" },
            {"the", "el"},
            {"be"  , "ser"},
            {"to", "a"},
            {"of", "de"},
            {"and", "y"},
            {"a", "un"},
            {"in", "en"},
            {"that", "ese"},
            {"have", "tener"},
            {"I", "yo"},
            {"it", "eso"},
            {"for", "para"},
            {"not", "no"},
            {"on", "en"},
            {"with", "con"},
            {"he", "él"},
            {"as", "como"},
            {"you", "tú"},
            {"do", "hacer"},
            {"at", "a"},
            {"this", "esta"},
            {"but", "pero"},
            {"his", "su"},
            {"by", "por"},

            {"from", "de"},
            {"they", "ellos"},
            {"we", "nosotros"},
            {"say", "decir"},
            {"her", "su"},
            {"she", "ella"},
            {"or", "o"},
            {"an", "un"},
            {"will", "será"},
            {"my", "mi"},
            {"one", "uno"},
            {"all", "todas"},
            {"would", "haría"},
            {"there", "ahí"},
            {"their", "su"},
            {"what", "qué"},
            {"so", "asi que"},
            {"up", "arriba"},
            {"out", "fuera"},
            {"if", "si"},
            {"about", "acerca de"},
            {"who", "quien"},
            {"get", "obtener"},
            {"which", "cual"},
            {"go", "ir"},
            {"me", "yo"},
            {"when", "cuando"},
            {"make", "hacer"},
            {"can", "poder"},
            {"like", "me gusta"},
            {"time", "hora"},
            {"no", "no"},
            {"just", "sólo"},
            {"him", "él"},
            {"know", "saber"},
            {"take", "tomar"},
            {"person", "persona"},
            {"into", "dentro"},
            {"year", "año"},
            {"your", "tu"},
            {"good", "bueno"},
            {"some", "algunos"},
            {"could", "podría"},
            {"them", "ellos"},
            {"see", "ver"},
            {"other", "otro"},
            {"than", "que"},
            {"then", "luego"},
            {"now", "ahora"},
            {"look", "Mira"},
            {"only", "solamente"},
            {"come", "ven"},
            {"its", "sus"},
            {"over", "encima"},
            {"think", "pensar"},
            {"also", "además"},
            {"back", "espalda"},
            {"after", "después"},
            {"use", "utilizar"},
            {"two", "dos"},
            {"how", "cómo"},
            {"our", "nuestra"},
            {"work", "trabajo"},
            {"first", "primero"},
            {"well", "bien"},
            {"way", "camino"},
            {"even", "incluso"},
            {"want", "querer"},
            {"because", "porque"},
            {"any", "alguna"},
            {"these", "estas"},
            {"give", "dar"},
            {"day", "día"},
            {"most", "más"},
            {"us", "nos"}
        };
        */
    }

    private string wordDict(string word)
    {
        if (words.ContainsKey(word))
            return words[word];
        else
            return word.ToLower() + "o";
    }

    
    #endregion //PRIVATE_METHODS
}

