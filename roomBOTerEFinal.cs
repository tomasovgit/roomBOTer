#define debug //Runs against local evaluator. Comment to rock panaxeo.
#define verbose //Prints some verbose statistics. Useful to get some insights.
/***********************************************
* 
* wordle bot maslo a.k.a. roomboter (c) TK
* 
* ********************************************/
//https://wordle.panaxeo.com/register/<your bot>/<your mail>/
//{ "token":"your token"}
//https://wordle.panaxeo.com/start/<token>/<wordlen>
//{"candidate_solutions":["jive","imra",...,"urci"],"gameid":"id"}
//https://wordle.panaxeo.com/guess/<id>/srna/
//{"result":"N_NN"}
using System.Text;
#if !debug
using Newtonsoft.Json.Linq;
// roomBOTer token
const string token = "your token";
#endif
double score = 0;
bot b = new bot();
// running average of 256 games
double[] res256 = new double[256];
for (int i = 0; i < 256; i++) { res256[i] = 10; }
double min256 = 10.0;
int resInd = 0;
/*****************************************************************/
// safety stop after score reached
// 4
const double target = 4.156;
// 5
//const double target = 3.414;
// 6
//const double target = 3.027;			
const int lenWord = 4;			// word length (4,5,6)
/*****************************************************************/
b.Init(lenWord);
// N games play loop
for (int a = 1; a <= 10000; a++)
{
#if debug
	b.readWords();
	string solution = b.makeSolution();
#else
	b.startGame(token);
#endif
	int i = 0;
	string response;
	do
	{
		i++;
		response = b.doGuessAndProcess(i);
		b.lastResponse = response;
	}
	while ((response.Contains('N') || response.Contains('_')) && i < 10); // 10 is max attempts by definition
	score += i;

	// calculation of runnig average
	res256[resInd] = i;
	resInd++;
	if (resInd == 256)
		resInd = 0;
	double avg256 = 0;
	for (int aa = 0; aa < 256; aa++)
	{
		avg256 += res256[aa];
	}
	avg256 /= 256.0;
	if (min256 > avg256)
	{
		min256 = avg256;
	}
#if debug
	Console.WriteLine(a + "," + i + "," + solution + "," + Math.Round(score / a, 3).ToString().Replace(',', '.') + "," + Math.Round(avg256, 3).ToString().Replace(',', '.') + "," + Math.Round(min256, 3).ToString().Replace(',', '.'));
#else
	Console.WriteLine(a + "," + i + "," + response + "," + Math.Round(score / a, 3).ToString().Replace(',', '.') + "," + Math.Round(avg256, 3).ToString().Replace(',', '.') + "," + Math.Round(min256, 3).ToString().Replace(',', '.'));
#endif
	if (Console.KeyAvailable) // give it a chance to stop it manually without hiting 10. 
	{
		Console.ReadLine();
	}
	if (avg256 < target) // be careful while this bot is a winner :)
	{
		Console.ReadLine(); // continue manually, only on own risk
	}
}
Console.ReadLine();
/// <summary>
/// roomBOTer
/// </summary>
class bot
{
	const int maxWords = 3200;
	// N chars word to guess
	const string allChars = "abcdefghijklmnopqrstuvwxyz";
	int maxBruteForceChars = 0; // cap on how many distinct chars to use for Rumburak (see below)
	Random rnd = new Random();
	string guessed = "";
	public string lastResponse = "";
	string[] responsesAr = new string[0];
	List<string> setPickFrom = new(); // set of words to evaluate for the best guess
	int lenWord = 0;
	// a set of possible solutions
	List<string> selectedWords = new();
#if debug
	readonly evaluator e = new evaluator();
	string[] allwords = new string[0]; // set of all words to take maxWords randomly from (debug purposes)
#endif
	/// <summary>
	/// set caps to force it to perform
	/// </summary>
	public void Init(int lenWord)
	{
		this.lenWord = lenWord;
		responsesAr = File.ReadAllLines(@"./data/wordleans" + lenWord + ".txt")[0].Split(' '); // precomputed YN_ combinations
#if debug
		allwords = File.ReadAllLines(@"./data/words" + lenWord + ".txt");
#endif
		// keep it below 100k
		if (lenWord == 4)
		{
			maxBruteForceChars = 16;
		}
		if (lenWord == 5)
		{
			maxBruteForceChars = 13;
		}
		if (lenWord == 6)
		{
			maxBruteForceChars = 9;
		}
	}
#if debug
	string solution = "";
#endif
	int countWords = maxWords;
#if debug
	/// <summary>
	/// create set of solutions of the predefined (larger) set
	/// </summary>
	public void readWords()
	{
		selectedWords.Clear();
		int i = 0;
		do
		{
			int p = rnd.Next(allwords.Length);
			if (!selectedWords.Contains(allwords[p]))
			{
				selectedWords.Add(allwords[p]);
				i++;
			}
		}
		while (i < maxWords);
	}
	/// <summary>
	/// pick a solution for debug purposes
	/// </summary>
	public string makeSolution()
	{
		this.solution = selectedWords.ElementAt(rnd.Next(selectedWords.Count));
		return this.solution;
	}
#else
	readonly HttpClient hc = new HttpClient();
	public string gameid = "";
	public void startGame(string token)
	{
		string repsonse = "";
		HttpRequestMessage hm = new HttpRequestMessage();
		Uri uri = new Uri(@"http://wordle.panaxeo.com/start/" + token + "/" + lenWord + "/");
		hm.RequestUri = uri;
		hm.Method = HttpMethod.Get;
		// boo for a sync call
		HttpResponseMessage mess = hc.Send(hm);
		Stream stream = mess.Content.ReadAsStream();
		stream.Position = 0;
		using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
		{
			repsonse = reader.ReadToEnd();
		}
		JObject joResponse = JObject.Parse(repsonse);
		JArray arrayCS = (JArray)joResponse["candidate_solutions"];
		selectedWords = new List<string>(arrayCS.Select(jv => (string)jv).ToArray());
		this.gameid = (string)joResponse["gameid"];
	}
#endif
	/// <summary>
	/// Take word with highest rank. Use combinations of distinct chars union lexicon words to find a word with best rank by entropy
	/// </summary>
	/// <returns></returns>
	public string makeBestProbabilityGuess(int attempt)
	{
		setPickFrom = selectedWords;
		string alphabet = "";
		if (attempt == 1) // speed it up by using n precalculated best guesses (these do not vary that much by randomizing 3200 among all the lexicon.
										  // To take 256 best ones for one run is good enough to feed any run. e.g. for 4 chars it is 99% sroa,
										  // and 99.999% it takes one among first 16 words of precomputed set, so the 256 is even an overkill)
		{
			string[] topOnes = File.ReadAllLines(@"./data/words" + lenWord + "top256.txt");
			// do not even concatenate with lexicon words - 99.999% the word is from top 16 of this top 256
			setPickFrom = new List<string>(topOnes);
		}
		else
		{
			alphabet = String.Join("", String.Join("", setPickFrom.ToArray()).Distinct()); // distinct chars of all remaining words
			string firstWord = setPickFrom.OrderBy(x => x).ElementAt(0);
			// create 1st combination to start from as 1st char (sorted) repeat lenWord times
			char firstChar = firstChar = firstWord[0];
			string startStr = "";
			for (int i = 0; i < lenWord; i++)
			{
				startStr += firstChar;
			}
			// get all distinct combinations using <alphabet>, starting with <firstWord>
			alphabet = String.Join("", alphabet.ToArray().OrderBy(x => x));
			// too many combinations, take less by most probable chars
			if (alphabet.Length > maxBruteForceChars)
			{
				alphabet = buildCharsProbability()[..maxBruteForceChars];
				alphabet = String.Join("", alphabet.ToArray().OrderBy(x => x));
			}
			List<string> allWordsCombinationl = Rumburak.GetWords(alphabet, startStr);
			// concatenate with remaining lexicon words to get better set to search for best entropy
			setPickFrom = setPickFrom.Concat(allWordsCombinationl).ToList();
		}
		setPickFrom = getBestByEntropy(setPickFrom, selectedWords);
		string bestGuess = setPickFrom.ElementAt(0);
#if verbose
		Console.Write(((attempt > 1) ? "Chars combined: " + alphabet.Length + ". " : "") + "Words checked: " + setPickFrom.Count + ". Loops ran: " + (setPickFrom.Count * responsesAr.Length) + ".");
		// say hooray the Extended guess surpassed Lexicon guess
		Console.WriteLine(selectedWords.Contains(bestGuess) ? "" : " Nonlexicon guess.");
#endif
		return bestGuess;
	}
	/// <summary>
	/// main loop : guess, get response, reduce set and again and again
	/// </summary>
	/// <param name="attempt"></param>
	/// <returns></returns>
	public string doGuessAndProcess(int attempt)
	{
		countWords = selectedWords.Count;
#if verbose
		Console.WriteLine();
		Console.WriteLine("Attempt: " + attempt);
#endif
		guessed = makeBestProbabilityGuess(attempt);
#if debug
		string response = e.evaluate(solution, guessed);
#else
		string response = getResponse();
#endif
#if verbose
		Console.WriteLine("COUNT    " + countWords);
		Console.WriteLine("GUESS    " + guessed);
		Console.WriteLine("RESP     " + response);
#endif
		// basic removal of possibilities based on response
		selectedWords = selectedWords.Where(x => passesThru(guessed, x, response)).ToList();
		this.lastResponse = response;
		countWords = selectedWords.Count;
		return response;
	}
	/// <summary>
	/// Condition to use in lambda to filter list of words. True if word passes [guess vs response]
	/// </summary>
	/// <param name="guess"></param>
	/// <param name="word">word from list</param>
	/// <param name="response"></param>
	/// <returns></returns>
	private bool passesThru(string guess, string word, string response)
	{
		for (int i = 0; i< guess.Length; i++)
		{
			switch (response[i])
			{
				case 'Y': if ((guess[i] != word[i]))
						return false;
					break;
				case 'N':
					if (word.Contains(guess[i]))
						return false;
					break;
				case '_':
					if ((guess[i] == word[i]) || !word.Contains(guess[i]))
						return false;
					break;
			}
		}
		return true;
	}
	/// <summary>
	/// build a string of chars sorted by descending frequency in remaining words (first char in returned string is char with most occurances, etc)
	/// </summary>
	/// <returns></returns>
	private string buildCharsProbability()
	{
		List<Tuple<int, char>> charCounts = new();
		string words = String.Join("", selectedWords);
		for (int allC = 0; allC < allChars.Length; allC++)
		{
			int cntChars = words.ToCharArray().Where(x => x == allChars[allC]).Count();
			charCounts.Add(new Tuple<int, char>(cntChars, allChars[allC]));
		}
		return String.Join("", charCounts.OrderByDescending(x => x.Item1).Select(x=>x.Item2));
	}
	/// <summary>
	/// returns set of words sorted by entropy
	/// </summary>
	/// <param name="setFrom"></param>
	/// <param name="setOver"></param>
	/// <returns></returns>
	private List<string> getBestByEntropy(List<string> setFrom, List<string> setOver)
	{
		List<Tuple<int, double>> listBest = new();
		for (int i = 0; i < setFrom.Count; i++) 
		{
			string word = setFrom.ElementAt(i);
			double entropy = getEntropy(word, setOver);
			listBest.Add(new Tuple<int, double>(i, entropy));
		}
		// select best ones 
		List<string> bestSet = listBest.OrderByDescending(x => x.Item2).Select(x => setFrom.ElementAt(x.Item1)).ToList();
		return bestSet;
	}
	/// <summary>
	/// Mr Shannon's entropy. Better than "expectation of words to be removed" (= when Log replaced by '(double)set.Count - (double)cloneCount') and of course not negative).
	/// </summary>
	/// <param name="word"></param>
	/// <param name="set"></param>
	/// <returns></returns>
	private double getEntropy(string word, List<string> set)
	{
		double entropy = 0.0;
		// for all possible responses
		Parallel.For(0, responsesAr.Length, a =>
		{
		   int cloneCount = selectedWords.Where(x => passesThru(word, x, responsesAr[a])).Count();
		   if (cloneCount > 0)
		   {
			   double probabilityThis = (double)cloneCount / (double)set.Count;
			   entropy += probabilityThis * Math.Log(probabilityThis);
		   }
		});
		return -entropy;
	}
#if !debug
	private string getResponse()
	{
		HttpRequestMessage hm = new HttpRequestMessage();
		Uri uri = new Uri(@"https://wordle.panaxeo.com/guess/" + this.gameid + "/" + guessed + "/");
		hm.RequestUri = uri;
		hm.Method = HttpMethod.Get;
		HttpResponseMessage mess = hc.Send(hm);
		Stream stream = mess.Content.ReadAsStream();
		stream.Position = 0;
		string httpResponse = "";
		using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
		{
			httpResponse = reader.ReadToEnd();
		}
		JObject joResponse = JObject.Parse(httpResponse);
		return (string)joResponse["result"];
	}
#endif
}
/// <summary>
/// provided by panaxeo
/// </summary>
public class evaluator
{
	public string evaluate(string solution, string guess)
	{
		string[] output = new string[solution.Length];
		for (int i = 0; i < solution.Length; i++)
		{
			if (solution[i] == guess[i])
				output[i] = "Y";
			else if (solution.Contains(guess[i]))
			{
				output[i] = "_";
			}
			else
				output[i] = "N";
		}
		return string.Join("", output);
	}
}
/// <summary>
/// Once upon a time I explained recursion to (my) kids. Let's reuse.
/// </summary>
public class Rumburak
{
	static string zaklinadlo = "";
	static string abeceda = "";
	static int[] indexy = new int[256];
	static List<string> allWords = new();
	static bool PrehodPismeno(int pozicia_v_zaklinadle)
	{
		if (indexy[pozicia_v_zaklinadle] >= abeceda.Length - 1)
		{
			indexy[pozicia_v_zaklinadle] = 0;
			if (pozicia_v_zaklinadle > 0)
			{
				return PrehodPismeno(pozicia_v_zaklinadle - 1);
			}
			else
			{
				return false;
			}
		}
		else
		{
			indexy[pozicia_v_zaklinadle]++;
		}
		return true;
	}
	public static List<string> GetWords(string alphabet, string startStr)
	{
		allWords = new();
		string abc = alphabet;
		if (abc != "")
		{
			abeceda = abc;
		}
		//Console.WriteLine("Inzenyre Zachariasi, dekuji, abeceda je " + abeceda);
		//Console.WriteLine("Inzenyre Zachariasi, zadejte zaklinadlo.");
		zaklinadlo = startStr;
		if (zaklinadlo == "")
		{
			zaklinadlo = abeceda;
		}
		//Console.WriteLine("Inzenyre Zachariasi, dekuji, zacinam zaklinadlem " + zaklinadlo);
		//Console.WriteLine("Zmacknete ENTER !");
		//Console.ReadLine();
		for (int i = 0; i < zaklinadlo.Length; i++)
		{
			for (int a = 0; a < abeceda.Length; a++)
			{
				if (zaklinadlo[i] == abeceda[a])
				{
					indexy[i] = a;
				}
			}
		}
		//Console.Write(zaklinadlo);
		//Console.Write(" ");
		int posledna_pozicia = zaklinadlo.Length - 1;
		StringBuilder sb = new StringBuilder("");
		while (PrehodPismeno(posledna_pozicia))
		{
			sb.Clear();
			for (int i = 0; i < zaklinadlo.Length; i++)
			{
				sb.Append(abeceda[indexy[i]]);
			}
			zaklinadlo = sb.ToString();
			// take only distinct chars words
			if (zaklinadlo.Distinct().Count() == zaklinadlo.Length)
			{
				//Console.Write(zaklinadlo);
				//Console.Write(" ");
				allWords.Add(zaklinadlo);
			}
		}
		//File.WriteAllText("allstr.txt", allstring);
		//Console.WriteLine();
		//Console.WriteLine("Inzenyre Zachariasi, hotovo.");
		//Console.ReadLine();
		return allWords;
	}
}