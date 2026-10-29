#include <draco/compression/expert_encode.h>
#include <draco/compression/decode.h>
#include <fstream>
#include <iostream>
#include <set>
#include <array>
#include <cmath>
#include <limits>
#include <vector>

// Small bridge to the system Draco library; the exporter supplies float attributes and indices.
int main(int argc, char **argv) {
  if (argc != 3) return 2;
  std::ifstream in(argv[1], std::ios::binary);
  auto read = [&](auto &value) { in.read(reinterpret_cast<char *>(&value), sizeof(value)); };
  uint32_t vertices, faces, attributes;
  read(vertices); read(faces); read(attributes);
  draco::Mesh mesh;
  mesh.set_num_points(vertices);
  std::set<std::array<float, 3>> positions;
  std::vector<int> bits;
  for (uint32_t a = 0; a < attributes; ++a) {
    uint32_t type, width, quantization;
    read(type); read(width); read(quantization);
    std::vector<float> data(vertices * width);
    in.read(reinterpret_cast<char *>(data.data()), data.size() * sizeof(float));
    draco::GeometryAttribute attribute;
    attribute.Init(static_cast<draco::GeometryAttribute::Type>(type), nullptr, width,
                   draco::DT_FLOAT32, false, width * sizeof(float), 0);
    const int id = mesh.AddAttribute(attribute, true, vertices);
    for (uint32_t v = 0; v < vertices; ++v) {
      mesh.attribute(id)->SetAttributeValue(draco::AttributeValueIndex(v), &data[v * width]);
      if (a == 0) positions.insert({data[v * width], data[v * width + 1], data[v * width + 2]});
    }
    mesh.attribute(id)->set_unique_id(a);
    bits.push_back(quantization);
  }
  for (uint32_t f = 0; f < faces; ++f) {
    uint32_t triangle[3];
    in.read(reinterpret_cast<char *>(triangle), sizeof(triangle));
    mesh.AddFace({draco::PointIndex(triangle[0]), draco::PointIndex(triangle[1]), draco::PointIndex(triangle[2])});
  }
  if (!in) return 3;
  draco::ExpertEncoder encoder(mesh);
  encoder.SetSpeedOptions(0, 0);
  for (uint32_t a = 0; a < attributes; ++a) encoder.SetAttributeQuantization(a, bits[a]);
  draco::EncoderBuffer buffer;
  const auto status = encoder.EncodeToBuffer(&buffer);
  if (!status.ok()) { std::cerr << status.error_msg_string(); return 4; }
  draco::DecoderBuffer source;
  source.Init(buffer.data(), buffer.size());
  draco::Decoder decoder;
  auto result = decoder.DecodeMeshFromBuffer(&source);
  if (!result.ok()) return 5;
  const auto &decoded = *result.value();
  auto *position = decoded.GetAttributeByUniqueId(0);
  const auto lo = positions.begin();
  float extent = 0;
  for (const auto &p : positions) for (int c = 0; c < 3; ++c) extent = std::max(extent, std::abs(p[c] - (*lo)[c]));
  const float tolerance = std::max(1e-5f, extent / (1 << (bits[0] - 1)));
  float max_error = 0;
  for (uint32_t v = 0; v < decoded.num_points(); ++v) {
    std::array<float, 3> point;
    position->ConvertValue<float, 3>(position->mapped_index(draco::PointIndex(v)), point.data());
    float error = std::numeric_limits<float>::infinity();
    for (auto it = positions.lower_bound({point[0] - tolerance, -INFINITY, -INFINITY});
         it != positions.end() && (*it)[0] <= point[0] + tolerance; ++it) {
      float e = 0;
      for (int c = 0; c < 3; ++c) e = std::max(e, std::abs((*it)[c] - point[c]));
      error = std::min(error, e);
    }
    max_error = std::max(max_error, error);
    if (error > tolerance) { std::cerr << "Position compression exceeded tolerance"; return 6; }
  }
  std::ofstream out(argv[2], std::ios::binary);
  out.write(buffer.data(), buffer.size());
  std::cout << decoded.num_points() << " " << max_error;
}
