using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FetchTcx
{
    class Formatter
    {
        const string tcxHeader = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<TrainingCenterDatabase xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:schemaLocation=\"http://www.garmin.com/xmlschemas/TrainingCenterDatabasev2.xsd\" xmlns=\"http://www.garmin.com/xmlschemas/TrainingCenterDatabase/v2\">\r\n";
        static string[] tcxActivityType = { "Biking", "Running", "Other", "MultiSport" };
        static string[] pwxActivityType = { "Bike", "Run", "Other", "Multi" };

        public static void WriteTcx(string fileName, Packet.Train train, string devName)
        {
            StreamWriter output = new StreamWriter(fileName);
            output.Write(tcxHeader);
            //TODO: Find activity automatically.
            string tcxActivity = "Biking";            
            output.Write("  <Activities>\r\n    <Activity Sport=\"" + tcxActivity + "\">\r\n");
            DateTime startTime = train.StartTime;
            output.Write("      <Id>" + startTime.ToString("yyyy-MM-ddTHH:mm:ss.f") + "</Id>\r\n");
            var segments = train.Laps;
            var samples = train.TrackPoints;
            TimeSpan lastLapTime = new TimeSpan();
            foreach (var segment in segments)
            {
                DateTime laptime = startTime + lastLapTime;
                lastLapTime = segment.LapTime;
                output.Write("      <Lap StartTime=\"" + laptime.ToString("yyyy-MM-ddTHH:mm:ss.f") + "\">\r\n");
                var duration = segment.LapTime;
                output.Write("        <TotalTimeSeconds>" + duration.TotalSeconds + "</TotalTimeSeconds>\r\n");
                var distance = segment.LapDistanceMeters;
                output.Write("        <DistanceMeters>" + distance + "</DistanceMeters>\r\n");
                var calories = segment.LapCalories;
                output.Write("        <Calories>" + calories + "</Calories>\r\n");
                output.Write("        <Intensity>Active</Intensity>\r\n");
                output.Write("        <TriggerMethod>Manual</TriggerMethod>\r\n");
                output.Write("        <Track>\r\n");
                double timevalueinmilliseconds = 0;
                foreach (var sample in samples)
                {
                    output.Write("          <Trackpoint>\r\n");
                    double timevalue = sample.IntervalTime;
                    timevalueinmilliseconds += (timevalue) * 1000;
                    DateTime time = laptime.AddMilliseconds(timevalueinmilliseconds);
                    output.Write("            <Time>" + time.ToString("yyyy-MM-ddTHH:mm:ss.f") + "</Time>\r\n");

                    var lat = sample.Latitude;
                    var lon = sample.Longitude;
                    output.Write("            <Position>\r\n");
                    output.Write("              <LatitudeDegrees>" + lat + "</LatitudeDegrees>\r\n");
                    output.Write("              <LongitudeDegrees>" + lon + "</LongitudeDegrees>\r\n");
                    output.Write("            </Position>\r\n");

                    var alt = sample.Altitude;
                    output.Write("            <AltitudeMeters>" + alt + ".00</AltitudeMeters>\r\n");
                    
                    var hr = sample.HeartRate;
                    if(hr != 0)
                        output.Write("            <HeartRateBpm><Value>" + hr + "</Value></AltitudeMeters>\r\n");
                    var cad = sample.Cadence;
                    output.Write("            <Cadence>" + cad + "</Cadence>\r\n");
                    output.Write("            <Extensions>\r\n");
                    output.Write("                        <TPX xmlns=\"http://www.garmin.com/xmlschemas/ActivityExtension/v2\">\r\n");
                    output.Write("                                    <Watts>" + sample.Power + "</Watts>\r\n");
                    output.Write("                        </TPX>\r\n");
                    output.Write("            </Extensions>\r\n");
                    output.Write("          </Trackpoint>\r\n");                    

                }
                output.Write("        </Track>\r\n");
                output.Write("      </Lap>\r\n");
            }
            var name = devName;
            output.Write("<Creator xsi:type=\"Device_t\">\r\n");
            output.Write("<Name>" + name + "</Name>\r\n");
            output.Write("<UnitId>None</UnitId>\r\n");
            output.Write("<ProductID>None</ProductID>\r\n");
            output.Write("<Version>\r\n");
            output.Write("<VersionMajor>1</VersionMajor>\r\n");
            output.Write("<VersionMinor>0</VersionMinor>\r\n");
            output.Write("<BuildMajor>0</BuildMajor>\r\n");
            output.Write("<BuildMinor>0</BuildMinor>\r\n");
            output.Write("</Version>\r\n");
            output.Write("</Creator>\r\n");
            output.Write("    </Activity>\r\n  </Activities>\r\n</TrainingCenterDatabase>\r\n");
            output.Close();            
        }        
    }
}
