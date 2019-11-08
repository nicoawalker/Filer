using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

using Common;

namespace TagEditor
{

	public class Tag : DBDataContainer<Tag>
	{
		public int ID { get; set; }

		public string Label { get; set; }

		public System.Windows.Media.Brush ColorBrush { get; set; }

		public List<int> Children { get; set; }		

		public Tag()
		{
			Children = new List<int>();
			ColorBrush = (SolidColorBrush)new BrushConverter().ConvertFrom("#00000000");
			ID = -1;
			Label = "";
		}

		public override int PropertyCount()
		{
			return 4;
		}

		public override string this[string propertyName]
		{
			set
			{
				switch ( propertyName.ToLower() )
				{
					case "id":
						{
							ID = Int32.Parse(value);
							break;
						}
					case "color":
						{
							ColorBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(value);
							break;
						}
					case "label":
						{
							Label = value;
							break;
						}
					case "children":
						{
							//convert string of child ids ("1,2,3,...)" into list of ids
							List<string> splitString = value.Split(',').ToList();
							foreach(string input in splitString)
							{
								int convertedInput = 0;
								if(Int32.TryParse(input, out convertedInput))
								{
									Children.Add(convertedInput);
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
				switch(index)
				{
					case 0:
						return ID.ToString();
					case 1:
						return Label;
					case 2:
						return ColorBrush.ToString();
					case 3:
						return String.Join(",", Children);
					default: throw new IndexOutOfRangeException($"Tag does not have {index} parameters");
				}
			}
		}
	}

	public class TagCreator : DBDataContainerCreator<Tag>
	{
		public override Tag Create()
		{
			return new Tag();
		}
	}

}
