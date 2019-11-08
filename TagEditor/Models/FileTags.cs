using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Common;

namespace TagEditor
{
	public class FileTags : DBDataContainer<FileTags>
	{
		public string Path { get; set; }

		public List<int> Tags { get; set; }

		public FileTags()
		{
			Path = "";
			Tags = new List<int>();
		}

		public override int PropertyCount()
		{
			return 2;
		}

		public override string this[string propertyName]
		{
			set
			{
				switch ( propertyName.ToLower() )
				{
					case "path":
						{
							Path = value;
							break;
						}
					case "tags":
						{
							//convert string of child ids ("1,2,3,...)" into list of ids
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
					default: throw new InvalidOperationException($"Tag does not contain a property named '{propertyName}'");
				}
			}
		}

		public override string this[int index]
		{
			get
			{
				switch ( index )
				{
					case 0:
						return Path;
					case 1:
						return String.Join(",", Tags);
					default: throw new IndexOutOfRangeException($"Tag does not have {index} parameters");
				}
			}
		}
	}

	public class FileTagsCreator : DBDataContainerCreator<FileTags>
	{
		public override FileTags Create()
		{
			return new FileTags();
		}
	}
}
