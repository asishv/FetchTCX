using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FetchTcx
{
    class MainClass
    {
        static SerialPort sp;
        static void Open(string portName, int baudRate)
        {
            sp = new SerialPort(portName, baudRate);
            //sp.DataReceived += sp_DataReceived;
            sp.ReadTimeout = 5000;
            sp.WriteTimeout = 1000;
            sp.Open();
        }

        static Packet Write(Packet p)
        {
            byte[] data = p.ConstructPayload(true);
            sp.Write(data, 0, data.Length);

            GB580P recieved = new GB580P();
            int command = sp.ReadByte();
            if (command >= 0 && command <= 255)
            {
                recieved.CommandId = (byte)command;
                int hiPacketLen = sp.ReadByte();
                int loPacketLen = sp.ReadByte();
                recieved.PacketLength = (Int16)((hiPacketLen << 8) + loPacketLen);
                recieved.PacketData = new byte[recieved.PacketLength];
                for (int i = 0; i < recieved.PacketLength; i++)
                    recieved.PacketData[i] = (byte)sp.ReadByte();
                int checksum = (byte)sp.ReadByte();
            }
            return recieved;
        }

        public enum ReadMode
        {
            Header = 0x0,
            Laps = Packet.HeaderTypeLaps,
            Points = Packet.HeaderTypeTrackPoints
        }

        static IList<Packet.Train> GetAllTrainings(GB580P device)
        {
            Packet p, r;
            //Get Track Headers
            p = device.GetTrackFileHeaders();
            r = Write(p);
            IList<Packet.TrackFileHeader> tracks = r.UnpackTrackHeaders();

            //Get Track Sections
            float totalPoints = 0;
            IList<Int16> trackIndexes = new List<Int16>();
            foreach (Packet.TrackFileHeader header in tracks)
            {
                totalPoints += header.TrackPointCount;
                //track number, less than 100
                trackIndexes.Add((Int16)header.TrackPointIndex);
            }
            float totalPointsRead = 0;
            p = p.GetTrackFileSections(trackIndexes);
            r = Write(p);
            IList<Packet.Train> trains = new List<Packet.Train>();
            ReadMode readMode = ReadMode.Header;
            int trainLapsToRead = 0;
            int pointsToRead = 0;
            while (r.CommandId != Packet.CommandId_FINISH)
            {
                //Check that previous mode was finished, especially at corruptions there can be out of sync 
                if (readMode != ReadMode.Header)
                {
                    byte readMode2 = r.GetTrainContent();
                    if (readMode2 == Packet.HeaderTypeTrackPoints)
                    {
                        if (readMode != ReadMode.Points)
                        {
                            //TODO: Handle error 
                        }
                        readMode = ReadMode.Points;
                    }
                    else if (readMode2 == Packet.HeaderTypeLaps)
                    {
                        if (readMode != ReadMode.Laps)
                        {
                            //TODO: Handle error 
                        }
                        readMode = ReadMode.Laps;
                    }
                    else
                    {
                        if (readMode != ReadMode.Header)
                        {
                            //TODO: Handle error 
                            if (trains.Count > 0)
                            {
                                trains.RemoveAt(trains.Count - 1);
                            }
                        }
                        readMode = ReadMode.Header;
                    }
                }
                if (r.CommandId == Packet.CommandId_FINISH)
                {
                    break;
                }
                switch (readMode)
                {
                    case ReadMode.Header:
                        {
                            Packet.Train train = r.UnpackTrainHeader();
                            if (train != null)
                            {
                                trainLapsToRead = train.LapCount;
                                pointsToRead = train.TrackPointCount;
                                trains.Add(train);
                            }
                            readMode = ReadMode.Laps;
                            break;
                        }

                    case ReadMode.Laps:
                        {
                            Packet.Train currentTrain = trains[trains.Count - 1];
                            IList<Packet.Lap> laps = r.UnpackLaps();
                            foreach (Packet.Lap lap in laps) currentTrain.Laps.Add(lap);
                            trainLapsToRead -= laps.Count;
                            if (trainLapsToRead <= 0)
                            {
                                readMode = ReadMode.Points;
                            }
                            break;
                        }

                    case ReadMode.Points:
                        {
                            Packet.Train currentTrain = trains[trains.Count - 1];
                            IList<Packet.TrackPoint> points = r.UnpackTrackPoints();
                            foreach (Packet.TrackPoint point in points) currentTrain.TrackPoints.Add(point);
                            pointsToRead -= points.Count;
                            totalPointsRead += points.Count;
                            DateTime startTime = currentTrain.StartTime.ToLocalTime();
                            if (pointsToRead <= 0)
                            {
                                readMode = ReadMode.Header;
                            }
                            break;
                        }

                }

                //All requests are the same
                r = Write(p.GetNextTrackSection());
            }
            return trains;
        }

        static void loginStrava(string userName, string password)
        {
            HttpWebRequest httpWReq = (HttpWebRequest)WebRequest.Create(@"https://www.strava.com/api/v2/authentication/login");
            ASCIIEncoding encoding = new ASCIIEncoding();
            string postData = "email="+userName;
            postData += "&password="+password;
            byte[] data = encoding.GetBytes(postData);

            httpWReq.Method = "POST";
            httpWReq.ContentType = "application/x-www-form-urlencoded";
            httpWReq.ContentLength = data.Length;

            using (Stream stream = httpWReq.GetRequestStream())
            {
                stream.Write(data,0,data.Length);
            }

            HttpWebResponse response = (HttpWebResponse)httpWReq.GetResponse();

            string responseString = new StreamReader(response.GetResponseStream()).ReadToEnd();
            Console.WriteLine(responseString);
        }

        static void uploadToStrava(string fileName)
        {
            /*https://stravasite-main.pbworks.com/w/page/51754311/v2%20upload%20create
             * upload/create
                Sends the server GPS and altitude data used to create a new ride.
 
                URL:
                http://www.strava.com/api/v2/upload
 
                Format:
                json or multipart/form-data (recommended for formats other than JSON)
 
                HTTP Method:
                POST
 
                Requires Authentication:
                true 
 
                Parameters:
                token: Authentication token (see authenticate/login).
                id: Optional. Unique identifier for the activity. The response will parrot back this Id paramater. If no Id is defined than a new unique Id will be minted.
                data: Required (unless using points). Activity data, in either TCX, GPX, FIT, or JSON format. NOTE: JSON uses the points format defined below.
                type: Required (unless using points). Format type of activity data, either: 'tcx', 'gpx', 'fit', or 'json'.
                data_fields: Optional. Only relevant if type is JSON data. Defines the fields, in order, that comprise an array element [row] of JSON data.
                Default data_fields are ['time', 'latitude', 'longitude', 'elevation', 'h_accuracy', 'v_accuracy', 'cmd'] 
                Additional possible fields are heartrate, cadence, and watts.
                NOTE: time, latitude, and longitude are required.
                cmd is a special field for passing instructions. The only valid value here is 'paused', which indicates that the device was paused at this point.  A new lap will be introduced starting at this point, and continuing until the next point with a 'paused' cmd (or the end of the ride).
                h_accuracy is the horizontal accuracy of this point, defined as a radius in meters.  For example, a value of 10 would indicate that the given point was accurate within a 10m radius.
                v_accuracy is the vertical accuracy of this point, in meters.  For example, a value of 10 would indicate that the elevation of the given point was accurate +/- 10m.
                points: Optional. Activity data may be passed as either a set of JSON points, or as a data payload and corresponding type (see above). The points data is a two-dimensional JSON array of points.  A single element in the points array contains all the data for that snapshot in time. See 'data_fields' for a definition of field values contained in each array element [row].
                activity_type: Optional. Type of activity: 'ride', 'run', etc. Default is 'ride'.
                activity_name: Optional. Name of activity. Default is the date of the activity, e.g. 'Ride - 8/01/11'  
 
                Response:
                id: Id passed by the client. If no Id is defined than a new unique Id will be minted.
                upload_id: Id used to track the progress of the activity as it's being processed. 
 
                Sample Request:
                {
                  "id": "2008-06-16T13:36:35Z",
                  "type": "json",
                  "data_fields": ["time", "latitude", "longitude", "elevation", "h_accuracy", "v_accuracy"],
                  "data": [
                    ["2011-04-19T16:09:03-07:00", 37.7020145393908, -122.496896423399, 109, 10, 10],
                    ["2011-04-19T16:09:14-07:00", 37.7020145393908, -122.496896423399, 109, 10, 10],
                    ["2011-04-19T16:09:24-07:00", 37.7020145393908, -122.496896423399, 109, 10, 10]
                   ] 
                }
 
                Sample Response:
                {"upload_id": 123456, "id": "2008-06-16T13:36:35Z"}
 
                Example of uploading a JSON file via cURL (with the token in the JSON payload):
 
                curl -X POST -H 'Content-Type: application/json' --data @upload.json http://www.strava.com/api/v2/upload
             */
        }

        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                bool stravaLoggedIn = false;
                string comport = args[0];
                Open(comport, 115200);
                GB580P device = new GB580P();
                Packet p = device.GetWhoAmI();
                Packet r = Write(p);
                string deviceName = r.ByteArr2String(0, r.PacketData.Length);
                Console.WriteLine("Found " + deviceName + " on " + comport);
                p = device.GetSystemConfiguration();
                r = Write(p);
                SystemConfiguration config = r.ResponseGetSystemConfiguration();
                Console.WriteLine("Device Name:" + config.DeviceName + " Firmware:" + config.Firmware + " Route Count:" + config.PcRouteCount +" Waypoint Count:"+ config.WaypointCount);
                IList<Packet.Train> trains = GetAllTrainings(device);
                if (args.Length == 3)
                {
                    loginStrava(args[1], args[2]);
                    stravaLoggedIn = true;
                }
                foreach (Packet.Train t in trains)
                {
                    Console.WriteLine("Start Time:" + t.StartTime + " Total Time:" + t.TotalTime + " Distance:" + t.TotalDistanceMeters);
                    string tcxFileName = config.DeviceName + "_" + t.StartTime.ToString("yyyy_MM_dd_hh_mm_ss") + ".tcx";
                    Formatter.WriteTcx(tcxFileName, t, deviceName);
                    if(stravaLoggedIn)
                        uploadToStrava(tcxFileName);
                    
                }

            }
        }
    }
}
