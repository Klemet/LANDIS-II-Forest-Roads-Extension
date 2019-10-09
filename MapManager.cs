﻿//  Author: Clément Hardy
// With mant elements shamelessely copied from the corresponding class
// in the "Base Fire" extension by Robert M. Scheller and James B. Domingo

using Landis.SpatialModeling;
using System.IO;
using System.Collections.Generic;
using Landis.Core;
using Landis.Library.AgeOnlyCohorts;
using Landis.Library.Metadata;
using System;
using System.Diagnostics;
using System.Linq;


namespace Landis.Extension.ForestRoadsSimulation
{
	public class MapManager
	{
		// Cette fonction lit la carte qui se trouve à l'endroit donné par "Path".
		// Elle va mettre cette carte dans un dictionnaire contenu dans la classe "SiteVars".
		// SoilRegionsContainer is used to fill up the soils map.
		public static void ReadMap(string path, string variableName)
		{
			IInputRaster<UIntPixel> map;

			// If the parameter "none" has been given, and if we are talking about an optional raster,
			// then all of the values for the site will be filled with the default value.
			if (variableName != "InitialRoadNetworkMap")
			{
				if (path == "none" || path == "None" || path == "" || path == null)
				{
					foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
					{
						if (variableName == "CoarseElevationRaster") SiteVars.CoarseElevation[site] = 0;
						if (variableName == "FineElevationRaster") SiteVars.FineElevation[site] = 0;
						if (variableName == "CoarseWaterRaster") SiteVars.CoarseWater[site] = 0;
						if (variableName == "FineWaterRaster") SiteVars.FineWater[site] = 0;
						if (variableName == "SoilsRaster") SiteVars.Soils[site] = null;

						// PlugIn.ModelCore.UI.WriteLine("Just put value 0 in raster " + variableName);
					}
					// If that's the case, no need to go further in the function. we stop here.
					return;
				}
			}
			
			// We try to open the map; if it fails, we raise an exception
			try
			{
				map = PlugIn.ModelCore.OpenRaster<UIntPixel>(path);
			}
			catch (FileNotFoundException)
			{
				string mesg = string.Format("Error: The file {0} does not exist", path);
				throw new System.ApplicationException(mesg);
			}

			// We check if the map has the same dimensions has the main LANDIS-II landscape
			if (map.Dimensions != PlugIn.ModelCore.Landscape.Dimensions)
			{
				string mesg = string.Format("Error: The input map {0} does not have the same dimension (row, column) as the ecoregions map", path);
				throw new System.ApplicationException(mesg);
			}

			// We read the map by looking at each pixel one by one, and putting the value of this pixel
			// in the correct site.
			// CAREFUL : Pixels and sites are read by reading a column from top to bottom, and the column from left to right.
			// NOT like a text from left to right AND top to bottom ; more rather like the japanese writing, top to bottom AND left to right.
			using (map)
			{
				UIntPixel pixel = map.BufferPixel;

				foreach (Site site in PlugIn.ModelCore.Landscape.AllSites)
				{
					// In case of a problem in the value of the pixel.
					try { map.ReadBufferPixel(); }
					catch { throw new Exception("Forest Roads Simulation : ERROR : There was a problem while reading the value of raster "
												+ variableName + " at site at location : " + site.Location + 
												". The value might be too big or too little. Please check again."); }

					int pixelValue = (int)pixel.MapCode.Value;

					// To deal with problems of distorted No Value Data in rasters, which happen often
					if (pixelValue < 0) pixelValue = 0;

					if (variableName == "InitialRoadNetworkMap") SiteVars.RoadsInLandscape[site] = new RoadType(pixelValue);
					else if (variableName == "CoarseElevationRaster") SiteVars.CoarseElevation[site] = pixelValue;
					else if (variableName == "FineElevationRaster") SiteVars.FineElevation[site] = pixelValue;
					else if (variableName == "CoarseWaterRaster") SiteVars.CoarseWater[site] = pixelValue;
					else if (variableName == "FineWaterRaster") SiteVars.FineWater[site] = pixelValue;
					else if (variableName == "SoilsRaster") SiteVars.Soils[site] = SoilRegions.GetSoilRegion(pixelValue);
					else throw new Exception("Forest roads simulation : ERROR; VARIABLE NAME NOT RECOGNIZED FOR RASTER READING.");

					// PlugIn.ModelCore.UI.WriteLine("Just put value "+ pixelValue + " in raster " + variableName);
				}
			}
		}

		// Function used for debugging purposes. Directly read into the input map given by the user. Obsolete.
		public static int ReadAPixelOfTheMap(string path, Site site)
		{
			IInputRaster<UIntPixel> map;

			// We try to open the map; if it fails, we raise an exception
			try
			{
				map = PlugIn.ModelCore.OpenRaster<UIntPixel>(path);
			}
			catch (FileNotFoundException)
			{
				string mesg = string.Format("Error: The file {0} does not exist", path);
				throw new System.ApplicationException(mesg);
			}

			// We check if the map has the same dimensions has the main LANDIS-II landscape
			if (map.Dimensions != PlugIn.ModelCore.Landscape.Dimensions)
			{
				string mesg = string.Format("Error: The input map {0} does not have the same dimension (row, column) as the ecoregions map", path);
				throw new System.ApplicationException(mesg);
			}

			// We read the given pixel of the map
			using (map)
			{
				// We get the number of the site to know where the pixel to read is (from upper-left to lower-right)
				// CAREFUL : Pixels and sites are read by reading a row from top to bottom, and the column from left to right.
				// NOT like a text from left to right AND top to bottom ; more rather like the japanese writing, top to bottom AND left to right.
				int sitePixelNumber = (int)(site.Location.Column + ((site.Location.Row - 1) * map.Dimensions.Rows));
				UIntPixel pixel = map.BufferPixel;
				// We skip the pixels until finding the one we want
				for (int i = 0; i < sitePixelNumber; i++)
				{
					map.ReadBufferPixel();
				}

				return ((int)pixel.MapCode.Value);
			}
		}

		// Cette fonction va écrire la carte à l'endroit donné par "Path" (qui contient le nom du fichier .tif auquel on va rajouter le timestep).
		// Cette carte va contenir le réseau routier actuel au timestep donné
		public static void WriteMap(string path, ICore ModelCore)
		{
			// On écrit la carte output du réseau de routes
			path = (path.Remove(path.Length - 4)) + ("-" + ModelCore.CurrentTime + ".tif");
			using (IOutputRaster<BytePixel> outputRaster = ModelCore.CreateRaster<BytePixel>(path, ModelCore.Landscape.Dimensions))
			{
				BytePixel pixel = outputRaster.BufferPixel;
				foreach (Site site in ModelCore.Landscape.AllSites)
				{
					pixel.MapCode.Value = (byte)SiteVars.RoadsInLandscape[site].typeNumber;

					outputRaster.WriteBufferPixel();
				}
			}
		}

		/// <summary>
		/// Gets the 8 neighbours surounding a site as a list of sites.
		/// </summary>
		/// <returns>
		/// A list of sites containing the neighbouring sites of the given site. 
		/// </returns>
		/// /// <param name="givenSite">
		/// A site for which the neighbouring sites must be found.
		/// </param>
		/// /// <param name="onlyRoads">
		/// If true, only add neighbouring sites with a road on it to the resulting list.
		/// </param>
		public static List<Site> GetNeighbouringSites(Site givenSite, bool onlyRoads = false)
		{
			List<Site> listOfNeighbouringSites = new List<Site>();

			RelativeLocation[] neighborhood = new RelativeLocation[]
{
				new RelativeLocation(-1,  0),  // north
                new RelativeLocation(-1,  1),  // northeast
                new RelativeLocation( 0,  1),  // east
                new RelativeLocation( 1,  1),  // southeast
                new RelativeLocation( 1,  0),  // south
                new RelativeLocation( 1, -1),  // southwest
                new RelativeLocation( 0, -1),  // west
                new RelativeLocation(-1, -1),  // northwest
};

			int siteRow = givenSite.Location.Row;
			int siteColumn = givenSite.Location.Column;

			foreach (RelativeLocation relativeLocation in neighborhood)
			{
				Site neighbour = givenSite.GetNeighbor(relativeLocation);
				// Seems like the GetNeighbor function cannot check if the neighbour is part of the landscape. We have to check.
				if (neighbour.Landscape == null) continue;
				else if (onlyRoads) { if (SiteVars.RoadsInLandscape[neighbour].IsARoad) listOfNeighbouringSites.Add(neighbour); }
				else listOfNeighbouringSites.Add(neighbour);
			}

			return (listOfNeighbouringSites);
		}

		/// <summary>
		/// Creates a list of relative locations around a site that will be checked to see if their is a road at skidding distance
		/// from a given site.
		/// </summary>
		/// <returns>
		/// A list of relative locations.
		/// </returns>
		/// <param name="skiddingDistance">
		/// The skidding distance given as a parameter to the plugin.
		/// </param>
		/// <param name="ModelCore">
		/// The model's core framework.
		/// </param>
		public static List<RelativeLocation> CreateSkiddingNeighborhood(int skiddingDistance, ICore ModelCore)
		{
			List<RelativeLocation> relativeCircleNeighborhood = new List<RelativeLocation>();

			float landscapeResolution = ModelCore.CellLength;

			int squareSizeOfNeighborhood = (int)Math.Floor(skiddingDistance / landscapeResolution) + 1;

			for (int col = -squareSizeOfNeighborhood; col < squareSizeOfNeighborhood + 1; col++)
			{
				for (int row = -squareSizeOfNeighborhood; row < squareSizeOfNeighborhood + 1; row++)
				{
					// We avoid checking the reference cell.
					if (col != 0 || row != 0)
					{
						// We check the euclidian distance between centroids to know if a site is with the skidding distance of the reference site.
						if (Math.Sqrt(Math.Pow((row * landscapeResolution), 2) + Math.Pow((col * landscapeResolution), 2)) <= skiddingDistance)
						{
							// If it is, it will be part of the skidding neighborhood.
							relativeCircleNeighborhood.Add(new RelativeLocation(row, col));
						}
					}
				}
			}

			return (relativeCircleNeighborhood);
		}

		/// <summary>
		/// Checks in the skidding neighborhood of a site if there is an existing road.
		/// </summary>
		/// <returns>
		/// True if there is an existing road nearby; False overwise.
		/// </returns>
		/// <param name="skiddingNeighborhood">
		/// A list of relativeLocations that we will explore to check if there is a road on the corresponding sites.
		/// </param>
		/// <param name="site">
		/// A site for which we want to check if there is a road nearby.
		/// </param>
		public static bool IsThereANearbyRoad(List<RelativeLocation> skiddingNeighborhood, Site site)
		{
			bool isThereANearbyRoad = false;

			foreach (RelativeLocation relativeNeighbour in skiddingNeighborhood)
			{
				Site neighbour = site.GetNeighbor(relativeNeighbour);
				// Seems like the GetNeighbor function cannot check if the neighbour is part of the landscape. We have to check.
				if (neighbour.Landscape == null) continue;
				// If the neighbour site has a road in it, we can stop right there.
				if (SiteVars.RoadsInLandscape[neighbour].IsARoad)
				{
					isThereANearbyRoad = true;
					break;
				}
			}

			return (isThereANearbyRoad);
		}



		/// <summary>
		/// Get all of the sites that have a road (forest road, sawmill, etc.) on them.
		/// </summary>
		/// <returns>
		/// A list of "Site" objects that are associated with a road in the variable RoadsInLandscape.
		/// </returns>
		/// /// <param name="ModelCore">
		/// The model's core framework.
		/// </param>
		public static List<Site> GetSitesWithRoads(ICore ModelCore)
		{
			List<Site> listOfSitesWithRoads = new List<Site>();

			foreach (Site site in ModelCore.Landscape.AllSites)
			{
				if (SiteVars.RoadsInLandscape[site].IsARoad)
				{
					listOfSitesWithRoads.Add(site);
				}
			}

			return (listOfSitesWithRoads);
		}

		/// <summary>
		/// Check if there is at least a site with a main public road or a sawmill so that the harvested wood has a place to flow to, and the road network places to connect to.
		/// </summary>
		/// <returns>
		/// A boolean indicating if there is at least such a site.
		/// </returns>
		/// /// <param name="ModelCore">
		/// The model's core framework.
		/// </param>
		public static bool IsThereAPlaceForTheWoodToGo(ICore ModelCore)
		{
			bool placeDetected = false;

			foreach (Site site in ModelCore.Landscape.AllSites)
			{
				if (SiteVars.RoadsInLandscape[site].IsAPlaceForTheWoodToGo)
				{
					placeDetected = true;
					break;
				}
			}

			return (placeDetected);
		}

		/// <summary>
		/// Calculate the manhattan distance between two given sites
		/// </summary>
		/// <returns>
		/// A double giving the value of this distance. The distance has no units (1 = side of a pixel or site)
		/// </returns>
		public static double GetDistance(Site givenSite, Site otherSite)
		{
			return (Math.Sqrt(Math.Pow((givenSite.Location.Row - otherSite.Location.Row), 2.0) + Math.Pow((givenSite.Location.Column - otherSite.Location.Column), 2.0)));
			// Manhanttan distance - For different neighbourhood type ?
			// return (Math.Abs(givenSite.Location.Row - otherSite.Location.Row) + Math.Abs(givenSite.Location.Column - otherSite.Location.Column));
		}

		/// <summary>
		/// Calculate the manhattan distance to the closest road from the given site
		/// </summary>
		/// <returns>
		/// A double giving the value of this distance. The distance has no units (1 = side of a pixel or site)
		/// </returns>
		/// /// <param name="sitesWithRoads">
		/// A list of sites containing roads that we want to check. Used for better performance, instead of launching .GetSitesWithRoads every time the function is called.
		/// </param>
		/// /// <param name="givenSite">
		/// The site for which we want to have the distance to the nearest road.
		/// </param>
        /// /// <param name="connected">
		/// If true, the function will look for the nearest road indicated as "connected" to a place where the wood can be transported.
		/// </param>
		public static double DistanceToNearestRoad(List<Site> sitesWithRoads, Site givenSite, bool connected = true)
		{
			double minDistance = double.PositiveInfinity;

			foreach (Site otherSite in sitesWithRoads)
			{
				if (connected) if (!SiteVars.RoadsInLandscape[otherSite].isConnectedToSawMill) continue;

				double distanceBetweenSites = GetDistance(givenSite, otherSite);

				if (distanceBetweenSites > 0 && distanceBetweenSites < minDistance)
				{
					minDistance = distanceBetweenSites;
				}
			}

			return (minDistance);
		}

		/// <summary>
		/// Get all of the sites that have a road, and that are connected (8-site neighbourhood) to the given site.
		/// </summary>
		/// <returns>
		/// A list of sites with a road on them, and that are connected to this one.
		/// </returns>
		public static List<Site> GetAllConnectedRoads(Site givenSite)
		{
			HashSet<Site> listOfConnectedNeighborsWithRoads = new HashSet<Site>();

			HashSet<Site> openSearchList = new HashSet<Site>();
			HashSet<Site> closedSearchList = new HashSet<Site>();

			openSearchList.Add(givenSite);

			while (openSearchList.Count != 0)
			{
				Site currentSite = openSearchList.ToList()[0];
				openSearchList.Remove(currentSite);

				foreach (Site neighbour in GetNeighbouringSites(currentSite, true))
				{
					listOfConnectedNeighborsWithRoads.Add(neighbour);
					if(!closedSearchList.Contains(neighbour)) openSearchList.Add(neighbour);
				}

				closedSearchList.Add(currentSite);
			}

			return (listOfConnectedNeighborsWithRoads.ToList());
		}

		/// <summary>
		/// Get all of the sites that have been harvested recently by the harvest extension, and to which we need to create a road to.
		/// </summary>
		/// <returns>
		/// A list of sites to which a road must be built.
		/// </returns>
		/// /// <param name="ModelCore">
		/// The model's core framework.
		/// </param>
		/// /// <param name="Timestep">
		/// The timestep of the extension.
		/// </param>
		public static List<Site> GetAllRecentlyHarvestedSites(ICore ModelCore, int Timestep)
		{
			List<Site> listOfHarvestedSites = new List<Site>();

			foreach (Site site in ModelCore.Landscape.AllSites)
				{
					// Carefull : the time of last event is the timestep when the last harvest event happened; not the number of years SINCE the last event.
					int timeOfLastEvent = Landis.Library.HarvestManagement.SiteVars.TimeOfLastEvent[site];

					if (site.IsActive && (ModelCore.CurrentTime - timeOfLastEvent) < Timestep && timeOfLastEvent != -100)
					{
						listOfHarvestedSites.Add(site);
					}

				}

			return (listOfHarvestedSites);
		}

		/// <summary>
		/// Shuffle a list of given sites according to their distance to the closest road, and the heuristic given by the user.
		/// </summary>
		/// <returns>
		/// The shuffled list of sites.
		/// </returns>
		/// /// <param name="ModelCore">
		/// The model's core framework.
		/// </param>
		/// /// <param name="listOfSites">
		/// The list of sites to shuffle.
		/// </param>
		/// /// <param name="heuristic">
		/// The heuristic given by the user as a parameter to the extension.
		/// </param>
		public static List<Site> ShuffleAccordingToHeuristic(ICore ModelCore, List<Site> listOfSites, string heuristic)
		{
			List<Site> shuffledListOfSites = new List<Site>();
			List<Site> listOfSitesWithRoads = new List<Site>();

			if (heuristic == "Random")
			{
				Random random = new Random();
				listOfSites = listOfSites.OrderBy(site => random.Next()).ToList();
			}
			else if (heuristic == "Closestfirst")
			{
				// We update the list of sites with roads that we used before; but we want only the connected sites inside of it.
				listOfSitesWithRoads = MapManager.GetSitesWithRoads(ModelCore);
				// We use it to find the distance to the nearest road for each cell
				shuffledListOfSites = listOfSites.OrderBy(site => MapManager.DistanceToNearestRoad(listOfSitesWithRoads, site, true)).ToList();
			}
			else if (heuristic == "Farthestfirst")
			{
				// Same thing, but in reverse order.
				listOfSitesWithRoads = MapManager.GetSitesWithRoads(ModelCore);
				shuffledListOfSites = listOfSites.OrderByDescending(site => MapManager.DistanceToNearestRoad(listOfSitesWithRoads, site, true)).ToList();
			}
			else throw new Exception("Heuristic non recognized");

			return (shuffledListOfSites);
		}

	}
}

