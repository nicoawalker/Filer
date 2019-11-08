using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
	public class RawFileData : DBDataContainer<RawFileData>
	{
		public string ID { get; set; }
		public string Path { get; set; }
		public string FileName { get; set; }
		public int FileType { get; set; }
		public List<int> Tags { get; set; }
		public int AccessCount { get; set; }
		public bool IsFavorite { get; set; }
		public bool IsHidden { get; set; }

		public RawFileData()
		{
			ID = "NULL";
			Path = "";
			FileName = "";
			FileType = 0;
			Tags = new List<int>();
			AccessCount = 0;
			IsFavorite = false;
			IsHidden = false;
		}

		public RawFileData( int id, string path, string name, int type, List<int> tags, int accessCount, bool isFavorite, bool isHidden )
		{
			ID = (id == -1) ? "NULL" : id.ToString();
			Path = path;
			FileName = name;
			FileType = type;
			Tags = tags;
			AccessCount = accessCount;
			IsFavorite = isFavorite;
			IsHidden = isHidden;
		}

		public override string this[string propertyName]
		{
			set
			{
				switch(propertyName.ToLower())
				{
					case "id":
						{
							ID = value;
							break;
						}
					case "path":
						{
							Path = value;
							break;
						}
					case "name":
						{
							FileName = value;
							break;
						}
					case "type":
						{
							FileType = Int32.Parse(value);
							break;
						}
					case "tags":
						{
							//convert string of tag ids ("1,2,3,...)" into list of int ids
							List<string> splitString = value.Split(',').ToList();
							foreach ( string input in splitString )
							{
								int convertedInput = 0;
								if ( Int32.TryParse(input, out convertedInput) )
								{
									Tags.Add(convertedInput);
								}
							}
							break;
						}
					case "access_count":
						{
							AccessCount = Int32.Parse(value);
							break;
						}
					case "favorite":
						{
							IsFavorite = value.Equals("1") ? true : false;
							break;
						}
					case "hidden":
						{
							IsHidden = value.Equals("1") ? true : false;
							break;
						}
					default: throw new InvalidOperationException($"RawFileData does not contain a property called {propertyName}");
				}
			}
		}

		public override string this[int index]
		{
			get
			{
				switch(index)
				{
					case 0:
						return ID;
					case 1:
						return Path;
					case 2:
						return FileName;
					case 3:
						return FileType.ToString();
					case 4:
						return (Tags == null) ? "" : String.Join(",", Tags);
					case 5:
						return AccessCount.ToString();
					case 6:
						return IsFavorite ? "1" : "0";
					case 7:
						return IsHidden ? "1" : "0";
					default: throw new ArgumentOutOfRangeException($"RawFileData does not contain a property at index {index}"); 
				}
			}
		}

		public override int PropertyCount()
		{
			return 8;
		}
	}

	public class RawFileDataCreator : DBDataContainerCreator<RawFileData>
	{
		public override RawFileData Create()
		{
			return new RawFileData();
		}
	}

}
