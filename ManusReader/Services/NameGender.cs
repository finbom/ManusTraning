using System;
using System.Collections.Generic;
using System.Linq;
using ManusReader.Models;

namespace ManusReader.Services;

// Guesses the likely gender of a role based on its name. Covers Swedish and
// English/American names. Uses, in order:
//   1. Role words / titles (MOM, DAD, QUEEN, MAMMA, KUNGEN …)
//   2. A list of common first names
//   3. A light suffix heuristic (names ending in -a are usually female)
// Returns null when no confident guess can be made.
public static class NameGender
{
    public static VoiceGender? Guess(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return null;

        // Take the first word, drop punctuation, lower-case it.
        var first = roleName
            .Split(new[] { ' ', '(', ')', ',', '.', '-', '–', '/', '\'' },
                   StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.ToLowerInvariant();

        if (string.IsNullOrEmpty(first)) return null;

        if (FemaleWords.Contains(first)) return VoiceGender.Kvinna;
        if (MaleWords.Contains(first)) return VoiceGender.Man;

        if (FemaleNames.Contains(first)) return VoiceGender.Kvinna;
        if (MaleNames.Contains(first)) return VoiceGender.Man;

        // Suffix heuristic: most names ending in -a are female. Dictionary lookups
        // above already catch the common male exceptions (Joshua, Ola, Noa …).
        if (first.Length >= 3 && first.EndsWith("a", StringComparison.Ordinal))
            return VoiceGender.Kvinna;

        return null;
    }

    // ---------------------------------------------------------------- role words

    private static readonly HashSet<string> FemaleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Swedish
        "mamma", "mor", "morsa", "mormor", "farmor", "moster", "faster",
        "syster", "syrra", "dotter", "fru", "frun", "fröken", "tant",
        "dam", "damen", "flicka", "flickan", "tjej", "tjejen",
        "kvinna", "kvinnan", "prinsessa", "drottning", "drottningen",
        "häxa", "häxan", "fe", "fen", "madam", "gumma", "gumman",
        "jungfru", "brud", "bruden", "käring", "käringen", "mamsell",
        "barnmorska", "sköterska", "lärarinna", "väninna",
        // English
        "mother", "mom", "mum", "mommy", "mummy", "mama", "ma",
        "grandma", "grandmother", "granny", "nana", "grandmom",
        "sister", "sis", "daughter", "aunt", "auntie", "aunty",
        "mrs", "miss", "ms", "madam", "madame", "lady", "girl",
        "woman", "queen", "princess", "witch", "fairy", "maid",
        "nurse", "waitress", "actress", "widow", "bride", "wife",
        "niece", "mistress", "governess", "dame", "hag", "maiden",
        "duchess", "countess", "baroness", "goddess", "heroine", "nun",
    };

    private static readonly HashSet<string> MaleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Swedish
        "pappa", "far", "farsa", "farfar", "morfar", "morbror", "farbror",
        "bror", "brorsa", "son", "sonen", "herr", "herrn", "herren",
        "pojke", "pojken", "kille", "killen", "gubbe", "gubben",
        "man", "mannen", "prins", "prinsen", "kung", "kungen", "konung",
        "riddare", "riddaren", "onkel", "gosse", "gossen", "yngling",
        "dräng", "drängen", "karl", "karln", "karlen",
        "soldat", "kapten", "kaptenen",
        // English
        "father", "dad", "daddy", "papa", "pop", "pa",
        "grandpa", "grandfather", "granddad", "grandad", "gramps",
        "brother", "bro", "uncle", "mr", "sir", "lord",
        "boy", "king", "prince", "knight", "wizard", "groom",
        "husband", "nephew", "gentleman", "guy", "lad", "fellow",
        "widower", "waiter", "actor", "duke", "count", "baron",
        "mister", "monk", "priest", "hero", "emperor", "chap",
    };

    // ---------------------------------------------------------------- first names

    private static readonly HashSet<string> FemaleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- Swedish ---
        "anna", "maria", "eva", "karin", "kristina", "lena", "sara", "emma",
        "kerstin", "ingrid", "birgitta", "marie", "elisabeth", "linnéa", "linnea",
        "johanna", "susanne", "margareta", "hanna", "linda", "malin", "sofia",
        "camilla", "cecilia", "annika", "helena", "elin", "julia", "anette",
        "annette", "åsa", "monica", "carina", "ulla", "gun", "britt", "inger",
        "astrid", "sigrid", "greta", "stina", "lisa", "lotta", "klara", "clara",
        "alice", "ebba", "wilma", "maja", "alva", "agnes", "ella", "saga",
        "signe", "vera", "moa", "tuva", "ida", "nora", "stella", "nelly",
        "olivia", "isabelle", "isabella", "felicia", "frida", "jenny", "josefin",
        "matilda", "amanda", "rebecca", "rebecka", "therese", "viktoria", "victoria",
        "vendela", "tilda", "tova", "lova", "edith", "dagny", "maj", "märta",
        "berit", "gunilla", "solveig", "barbro", "ann", "yvonne", "pia", "petra",
        "nina", "gerd", "margit", "ingegerd", "ester", "ruth", "rut", "selma",
        "elsa", "siri", "elvira", "elise", "lovisa", "wilhelmina", "augusta",
        "beda", "hilda", "tyra", "ulrika", "désirée", "desiree",
        "rakel", "filippa", "alma", "iris", "leah", "lea",
        "molly", "ines", "thea", "tea", "elina", "emelie", "emilia",
        "ronja", "vilda", "freja", "estelle", "celine", "celina",
        "doris", "gunvor", "majken", "svea", "disa",
        // --- English / American ---
        "mary", "patricia", "jennifer", "elizabeth", "barbara", "susan",
        "jessica", "sarah", "karen", "nancy", "betty", "margaret", "sandra",
        "ashley", "kimberly", "emily", "donna", "michelle", "carol",
        "dorothy", "melissa", "deborah", "stephanie", "sharon", "laura",
        "cynthia", "kathleen", "amy", "angela", "shirley", "brenda",
        "pamela", "nicole", "helen", "samantha", "katherine", "christine",
        "debra", "rachel", "catherine", "carolyn", "janet", "heather",
        "diane", "virginia", "julie", "joyce", "kelly", "christina",
        "lauren", "joan", "evelyn", "judith", "megan", "andrea", "cheryl",
        "hannah", "jacqueline", "martha", "gloria", "teresa", "madison",
        "frances", "kathryn", "janice", "jean", "abigail", "judy",
        "sophia", "grace", "denise", "amber", "marilyn", "danielle",
        "beverly", "theresa", "diana", "natalie", "brittany", "charlotte",
        "kayla", "alexis", "lori", "chloe", "ava", "mia", "scarlett",
        "lily", "zoe", "zoey", "ruby", "kate", "katie", "anne", "jane",
        "jill", "joanna", "polly", "lucy", "daisy", "rose", "rosie",
        "beth", "bella", "maggie", "vivian", "vivien", "penny", "penelope",
        "eleanor", "florence", "audrey", "gwen", "hazel", "ivy", "pearl",
        "gabrielle", "gabriella", "natasha", "vanessa", "veronica", "wendy",
        "paula", "rita", "gail", "joanne", "dawn", "tracy", "tina",
        "kelsey", "courtney", "crystal", "tiffany", "holly", "naomi",
        "ramona", "harriet", "agatha", "gertrude", "mabel", "ethel",
        "edna", "phyllis", "lorraine", "rosa", "carmen", "lola",
        "delia", "fiona", "sandy", "sally", "sue", "liz", "lizzie",
        "becky", "cathy", "ginny", "trixie", "vicky", "abby", "annie",
        "ellie", "evie", "millie", "tilly", "cassie", "carrie",
    };

    private static readonly HashSet<string> MaleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- Swedish ---
        "erik", "lars", "karl", "carl", "anders", "per", "johan", "nils",
        "lennart", "mikael", "michael", "jan", "hans", "olof", "olov", "ola",
        "sven", "bengt", "bo", "gunnar", "mats", "fredrik", "magnus", "andreas",
        "daniel", "mattias", "mathias", "marcus", "markus", "henrik", "stefan",
        "thomas", "tomas", "peter", "gustav", "gustaf", "oskar", "oscar",
        "william", "liam", "lucas", "lukas", "elias", "hugo", "alexander",
        "axel", "oliver", "emil", "noah", "noa", "filip", "philip", "adam",
        "isak", "anton", "albin", "ludvig", "viktor", "victor", "leo", "arvid",
        "sixten", "vincent", "theo", "folke", "gösta", "harald", "holger",
        "ivar", "sigurd", "sten", "stig", "tage", "torsten", "ture", "valter",
        "walter", "åke", "östen", "ragnar", "roland", "roger", "rolf", "rune",
        "börje", "kjell", "kurt", "leif", "ove", "verner", "yngve", "allan",
        "arne", "birger", "evert", "georg", "helge", "hjalmar", "jonas",
        "joel", "jonathan", "samuel", "simon", "sebastian", "robin", "robert",
        "richard", "rikard", "patrik", "pontus", "rasmus", "martin", "kristoffer",
        "christoffer", "christian", "kristian", "david", "benjamin", "gabriel",
        "melker", "milo", "loke", "alvar", "elis", "vidar",
        "edvin", "edvard", "edward", "frans", "valdemar", "vilhelm", "wilhelm",
        "casper", "kasper", "conny",
        // --- English / American ---
        "james", "robert", "john", "michael", "charles", "joseph",
        "christopher", "matthew", "anthony", "mark", "donald", "steven",
        "paul", "andrew", "joshua", "kenneth", "kevin", "brian", "george",
        "timothy", "ronald", "jason", "jeffrey", "ryan", "jacob", "gary",
        "nicholas", "eric", "stephen", "larry", "justin", "scott", "brandon",
        "gregory", "frank", "raymond", "patrick", "jack", "dennis", "jerry",
        "tyler", "aaron", "jose", "henry", "douglas", "nathan", "zachary",
        "kyle", "harold", "jeremy", "ethan", "keith", "gerald", "terry",
        "sean", "shawn", "arthur", "austin", "lawrence", "jesse", "joe",
        "bryan", "billy", "jordan", "albert", "dylan", "bruce", "willie",
        "logan", "alan", "juan", "wayne", "roy", "ralph", "randy", "eugene",
        "russell", "elijah", "louis", "bobby", "johnny", "mason", "caleb",
        "ronan", "declan", "tom", "tommy", "bill", "bob", "rob", "robbie",
        "mike", "mikey", "dave", "davey", "jim", "jimmy", "joey", "charlie",
        "chuck", "ed", "eddie", "ted", "teddy", "fred", "freddy", "harry",
        "hank", "jake", "sammy", "steve", "tony", "andy", "danny", "will",
        "nick", "nate", "pete", "ben", "gabe", "max", "matt", "chris",
        "greg", "phil", "ron", "ronnie", "don", "donnie", "stan", "walt",
        "gus", "archie", "frankie", "marv", "vince", "vinny", "ray", "sid",
        "clarence", "herbert", "norman", "leonard", "earl", "floyd",
        "clyde", "marvin", "vernon", "wesley", "morris", "gordon",
        "stewart", "stuart", "neil", "graham", "trevor", "clive", "nigel",
        "dexter", "felix", "rufus", "monty", "reggie",
    };
}
